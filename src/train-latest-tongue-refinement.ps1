param(
    [string]$SessionPath = "",
    [ValidateRange(1, 100)]
    [int]$Epochs = 24,
    [ValidateRange(8, 256)]
    [int]$BatchSize = 64
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    function Test-QproTrainingPython([string]$Candidate) {
        if ([string]::IsNullOrWhiteSpace($Candidate) -or -not (Test-Path -LiteralPath $Candidate -PathType Leaf)) { return $false }
        $previousPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            & $Candidate -c "import sys,cv2,numpy,torch; assert sys.version_info[:2] == (3,12) and sys.maxsize > 2**32" *> $null
            return $LASTEXITCODE -eq 0
        } catch { return $false }
        finally { $ErrorActionPreference = $previousPreference }
    }

    $python = $null
    Remove-Item Env:QPRO_ROCM_INSTALL_SMOKE_TEST -ErrorAction SilentlyContinue
    Remove-Item Env:QPRO_ROCM_EXPECTED_GFX_TARGET -ErrorAction SilentlyContinue
    $rocmCandidates = @(
        [pscustomobject]@{
            Name = 'AMD ROCm 10.0'
            Python = (Join-Path $PSScriptRoot '.venv-rocm-experimental\Scripts\python.exe')
            ReadyMarker = (Join-Path $PSScriptRoot '.venv-rocm-experimental\qpro-rocm-ready.json')
            TargetFamily = $null
        },
        [pscustomobject]@{
            Name = 'ROCm 7.2.1 fallback'
            Python = (Join-Path $PSScriptRoot '.venv-rocm\Scripts\python.exe')
            ReadyMarker = (Join-Path $PSScriptRoot '.venv-rocm\qpro-rocm-ready.json')
            TargetFamily = 'custom'
        }
    )
    foreach ($candidate in $rocmCandidates) {
        if (-not (Test-Path -LiteralPath $candidate.Python -PathType Leaf) -or
            -not (Test-Path -LiteralPath $candidate.ReadyMarker -PathType Leaf)) { continue }
        if ($candidate.TargetFamily) { $env:ROCM_SDK_TARGET_FAMILY = $candidate.TargetFamily }
        else { Remove-Item Env:ROCM_SDK_TARGET_FAMILY -ErrorAction SilentlyContinue }
        $probePreference = $ErrorActionPreference
        $probeSucceeded = $false
        try {
            $ErrorActionPreference = 'Continue'
            & $candidate.Python -c "import torch; from qpro_gpu import require_rocm_device_name; d=require_rocm_device_name(torch); print('Tongue training GPU:', torch.cuda.get_device_name(int(d.split(':')[1])), 'on', d)"
            $probeSucceeded = $LASTEXITCODE -eq 0
        } catch { Write-Warning "$($candidate.Name) validation failed: $_" }
        finally { $ErrorActionPreference = $probePreference }
        if ($probeSucceeded) {
            $python = $candidate.Python
            Write-Host "Selected $($candidate.Name) runtime."
            break
        }
        Write-Warning "$($candidate.Name) cannot see a supported discrete Radeon GPU. Trying the next runtime."
    }
    if (-not $python) {
        Remove-Item Env:ROCM_SDK_TARGET_FAMILY -ErrorAction SilentlyContinue
        $sharedPython = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'QproFaceTracking\runtime\.venv\Scripts\python.exe'
        $candidates = @(
            $env:QPRO_PYTHON,
            $sharedPython,
            (Join-Path $PSScriptRoot '.venv\Scripts\python.exe'),
            (Join-Path $PSScriptRoot '.venv\Scripts\qpro-python-console.exe')
        )
        foreach ($candidate in $candidates) {
            if (Test-QproTrainingPython $candidate) { $python = $candidate; break }
        }
        if (-not $python) { throw 'No working Qpro Python 3.12 training runtime was found. Run Install runtime in the Hub.' }
    }
    Write-Host "PC Python runtime: $python"

    $latest = if (-not [string]::IsNullOrWhiteSpace($SessionPath)) {
        Get-Item -LiteralPath ([System.IO.Path]::GetFullPath($SessionPath)) -ErrorAction Stop
    } else {
        Get-ChildItem .\captures -Filter "*.qpsession.json" -File |
            Sort-Object LastWriteTime -Descending |
            ForEach-Object {
                try {
                    $session = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
                    if ($session.sessionType -in @("tongue-stereo-corrections-v1", "tongue-stereo-refinement-v2", "tongue-stereo-arc-v3") -and $session.completed) { $_ }
                } catch {}
            } | Select-Object -First 1
    }
    if ($null -eq $latest) { throw "No completed quick-refinement capture was found." }
    $sessionMetadata = Get-Content -LiteralPath $latest.FullName -Raw | ConvertFrom-Json
    if ($sessionMetadata.sessionType -notin @("tongue-stereo-corrections-v1", "tongue-stereo-refinement-v2", "tongue-stereo-arc-v3") -or -not $sessionMetadata.completed) {
        throw "The selected dataset is not a completed quick-refinement capture."
    }
    $capture = $latest.FullName -replace '\.qpsession\.json$', '.qpcap'
    if (-not (Test-Path -LiteralPath $capture)) { throw "Matching capture is missing: $capture" }
    $cache = Join-Path $PSScriptRoot ("training\{0}-personal-refinement-224px" -f [System.IO.Path]::GetFileNameWithoutExtension($capture))
    Write-Host "TRAIN_STATUS phase=preparing"
    & $python .\prepare_tongue_stills.py $capture --session $latest.FullName --output $cache --size 224
    if ($LASTEXITCODE -ne 0) { throw "Preparing the refinement frames failed." }

    $pairs = @(Get-ChildItem .\models -Filter "qpro-stereo-tongue-v*-gate.pt" -File | ForEach-Object {
        if ($_.Name -match '^qpro-stereo-tongue-v(?<v>\d+)-gate\.pt$') {
            $v = [int]$Matches.v
            $direction = Join-Path $_.DirectoryName "qpro-stereo-tongue-v$v-direction.pt"
            if (Test-Path -LiteralPath $direction) { [pscustomobject]@{ Version=$v; Gate=$_.FullName; Direction=$direction } }
        }
    } | Sort-Object Version -Descending)
    if (-not $pairs.Count) { throw "No paired base tongue model was found." }
    $base = $pairs[0]
    # A failed earlier run may have left an unpaired checkpoint or TorchScript
    # companion. Allocate beyond every existing tongue-model artifact so a
    # retry cannot replace any personal weights or metadata.
    $occupiedVersions = @(Get-ChildItem .\models -Filter 'qpro-stereo-tongue-v*' -File |
        ForEach-Object {
            if ($_.Name -match '^qpro-stereo-tongue-v(?<v>\d+)(?:[.-]|$)') { [int]$Matches.v }
        })
    $version = ((@($base.Version) + $occupiedVersions | Measure-Object -Maximum).Maximum) + 1
    $gateOutput = ".\models\qpro-stereo-tongue-v$version-gate.pt"
    $directionOutput = ".\models\qpro-stereo-tongue-v$version-direction.pt"

    $deviceOutput = @(& $python -c "import torch; from qpro_gpu import preferred_torch_device_name; print(preferred_torch_device_name(torch))" | Select-Object -Last 1)
    $deviceExitCode = $LASTEXITCODE
    if ($deviceExitCode -ne 0 -or $deviceOutput.Count -eq 0) {
        throw "Could not determine the PyTorch training device (Python exit code $deviceExitCode). Check the preceding Activity output, then run PC runtime setup again."
    }
    $device = $deviceOutput[0].ToString().Trim()
    if ($device -ne 'cpu' -and $device -notmatch '^cuda:\d+$') { throw "Unsupported PyTorch training device: $device" }
    $effectiveBatchSize = if ($device -eq "cpu") { [Math]::Min($BatchSize, 16) } else { $BatchSize }
    Write-Host "TRAIN_DEVICE device=$device batch=$effectiveBatchSize"
    if ($device -eq "cpu") { Write-Warning "CUDA is unavailable. CPU fallback is active; training can take substantially longer. NVIDIA users should rerun PC runtime setup after installing the current NVIDIA driver." }

    Write-Host "TRAIN_STAGE index=1 total=2 name=visibility epochs=$Epochs device=$device"
    & $python .\train_tongue_model.py $cache --architecture spatial-stereo-resnet-v2 --checkpoint-focus visibility --initial-checkpoint $base.Gate --learning-rate 0.00005 --epochs $Epochs --batch-size $effectiveBatchSize --device $device --output $gateOutput
    if ($LASTEXITCODE -ne 0) { throw "Refining tongue visibility failed." }
    Write-Host "TRAIN_STAGE index=2 total=2 name=direction epochs=$Epochs device=$device"
    & $python .\train_tongue_model.py $cache --architecture spatial-stereo-resnet-v2 --checkpoint-focus direction --initial-checkpoint $base.Direction --learning-rate 0.00005 --epochs $Epochs --batch-size $effectiveBatchSize --device $device --output $directionOutput
    if ($LASTEXITCODE -ne 0) { throw "Refining tongue direction failed." }
    Write-Host "MODEL_READY version=$version parent=$($base.Version)"
}
finally {
    Pop-Location
}
