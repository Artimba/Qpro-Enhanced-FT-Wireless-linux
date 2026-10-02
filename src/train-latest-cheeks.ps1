param(
    [Parameter(Mandatory=$true)][string]$SessionPath,
    [Parameter(Mandatory=$true)][string]$BaseModelPath,
    [Parameter(Mandatory=$true)][string]$GateModelPath,
    [Parameter(Mandatory=$true)][ValidateRange(1,2147483647)][int]$Version,
    [ValidateRange(1,300)][int]$Epochs = 80,
    [ValidateSet('VirtualDesktop','SteamLink')][string]$TrackingSource = 'VirtualDesktop'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'runtime-python.ps1')
$visibilityNames = @('HIP_VISIBLE_DEVICES','CUDA_VISIBLE_DEVICES','ROCR_VISIBLE_DEVICES','GPU_DEVICE_ORDINAL')
$savedVisibility = @{}
foreach ($name in $visibilityNames) { $savedVisibility[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
$previousTarget = [Environment]::GetEnvironmentVariable('ROCM_SDK_TARGET_FAMILY','Process')
$savedSmoke = @{}
foreach ($name in @('QPRO_ROCM_INSTALL_SMOKE_TEST','QPRO_ROCM_EXPECTED_GFX_TARGET')) {
    $savedSmoke[$name] = [Environment]::GetEnvironmentVariable($name,'Process')
}
Push-Location $PSScriptRoot
try {
    foreach ($name in $savedSmoke.Keys) { [Environment]::SetEnvironmentVariable($name,$null,'Process') }
    $python = $null
    foreach ($candidate in @(Get-QproRocmCandidates $PSScriptRoot)) {
        if (-not (Test-Path -LiteralPath $candidate.ReadyMarker) -or -not (Test-Path -LiteralPath $candidate.Python)) { continue }
        foreach ($name in $visibilityNames) { [Environment]::SetEnvironmentVariable($name,$null,'Process') }
        [Environment]::SetEnvironmentVariable('ROCM_SDK_TARGET_FAMILY',$candidate.TargetFamily,'Process')
        $probePreference = $ErrorActionPreference
        $probeSucceeded = $false
        try {
            $ErrorActionPreference = 'Continue'
            $global:LASTEXITCODE = $null
            & $candidate.Python -c 'import torch; from qpro_gpu import require_rocm_device_name; print("Cheek training GPU:", require_rocm_device_name(torch))'
            $probeSucceeded = $LASTEXITCODE -eq 0
        } catch { Write-Warning "$($candidate.Name) validation failed: $_" }
        finally { $ErrorActionPreference = $probePreference }
        if ($probeSucceeded) { $python = $candidate.Python; break }
        Write-Warning 'This ROCm runtime did not pass device validation. Trying another runtime.'
    }
    if (-not $python) {
        foreach ($name in $visibilityNames) { [Environment]::SetEnvironmentVariable($name,$savedVisibility[$name],'Process') }
        [Environment]::SetEnvironmentVariable('ROCM_SDK_TARGET_FAMILY',$previousTarget,'Process')
        $candidates = @($env:QPRO_PYTHON,
            (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'QproFaceTracking\runtime\.venv\Scripts\python.exe'),
            (Join-Path $PSScriptRoot '.venv\Scripts\python.exe'))
        foreach ($candidatePython in $candidates) {
            if ([string]::IsNullOrWhiteSpace($candidatePython) -or -not (Test-Path -LiteralPath $candidatePython)) { continue }
            $probePreference = $ErrorActionPreference
            $probeSucceeded = $false
            try {
                $ErrorActionPreference = 'Continue'
                $global:LASTEXITCODE = $null
                & $candidatePython -c 'import sys,cv2,numpy,torch; assert sys.version_info[:2] == (3,12) and sys.maxsize > 2**32'
                $probeSucceeded = $LASTEXITCODE -eq 0
            } catch { Write-Warning "Training runtime check failed: $_" }
            finally { $ErrorActionPreference = $probePreference }
            if ($probeSucceeded) { $python = $candidatePython; break }
        }
    }
    if (-not $python -or -not (Test-Path -LiteralPath $python)) { throw 'Install the PC runtime in First-time setup before training camera cheeks.' }
    Write-Host 'TRAIN_STATUS phase=preparing'
    Write-Host "PC Python runtime: $python"
    & $python -u .\train_cheek_pair.py --session $SessionPath --parent-checkpoint $BaseModelPath --gate-checkpoint $GateModelPath --version $Version --epochs $Epochs --root $PSScriptRoot --source $TrackingSource
    if ($LASTEXITCODE -ne 0) { throw 'Camera cheek training failed. Your original tongue model was kept.' }
}
finally {
    foreach ($name in $visibilityNames) { [Environment]::SetEnvironmentVariable($name,$savedVisibility[$name],'Process') }
    [Environment]::SetEnvironmentVariable('ROCM_SDK_TARGET_FAMILY',$previousTarget,'Process')
    foreach ($name in $savedSmoke.Keys) { [Environment]::SetEnvironmentVariable($name,$savedSmoke[$name],'Process') }
    Pop-Location
}
