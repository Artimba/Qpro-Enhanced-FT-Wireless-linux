param(
    [string]$QproRoot = $PSScriptRoot,
    [switch]$UseLegacyRocm
)

$ErrorActionPreference = 'Stop'
$qproPrivatePython = Join-Path $env:LOCALAPPDATA 'QproFaceTracking\runtime\python-3.12.10\python.exe'
$qproSharedRuntime = Join-Path $env:LOCALAPPDATA 'QproFaceTracking\runtime'
$qproRequirements = Join-Path $QproRoot 'requirements-runtime.txt'
$qproGateModel = Join-Path $QproRoot 'models\qpro-stereo-tongue-v8-gate.pt'
$qproDirectionModel = Join-Path $QproRoot 'models\qpro-stereo-tongue-v8-direction.pt'
$qproStableRadeonNames = @(
    'Radeon RX 9070 XT', 'Radeon RX 9070', 'Radeon AI PRO R9700',
    'Radeon RX 9060 XT', 'Radeon RX 7900 XTX', 'Radeon PRO W7900',
    'Radeon PRO W7900 Dual Slot', 'Radeon RX 7700'
)
$qproRocm10Targets = @{
    'Radeon RX 6950 XT' = 'gfx1030'
    'Radeon RX 6900 XT' = 'gfx1030'
    'Radeon RX 6800 XT' = 'gfx1030'
    'Radeon RX 6800' = 'gfx1030'
    'Radeon RX 6750 XT' = 'gfx1031'
    'Radeon RX 6700 XT' = 'gfx1031'
    'Radeon RX 6700' = 'gfx1031'
    'Radeon RX 6650 XT' = 'gfx1032'
    'Radeon RX 6600 XT' = 'gfx1032'
    'Radeon RX 6600' = 'gfx1032'
    'Radeon RX 7900 XT' = 'gfx1100'
    'Radeon RX 7900 XTX' = 'gfx1100'
    'Radeon RX 7900 GRE' = 'gfx1100'
    'Radeon PRO W7900' = 'gfx1100'
    'Radeon PRO W7900 Dual Slot' = 'gfx1100'
    'Radeon RX 7800 XT' = 'gfx1101'
    'Radeon RX 7700 XT' = 'gfx1101'
    'Radeon RX 7700' = 'gfx1101'
    'Radeon RX 7600 XT' = 'gfx1102'
    'Radeon RX 7600' = 'gfx1102'
    'Radeon RX 9070 XT' = 'gfx1201'
    'Radeon RX 9070' = 'gfx1201'
    'Radeon RX 9070 GRE' = 'gfx1201'
    'Radeon AI PRO R9700' = 'gfx1201'
    'Radeon RX 9060 XT' = 'gfx1200'
    'Radeon RX 9060' = 'gfx1200'
}

if ([Environment]::OSVersion.Version.Build -lt 22000) {
    throw 'Qpro AMD ROCm requires Windows 11. Use the CPU runtime on this version of Windows.'
}

try {
    $qproVideoControllers = @(Get-CimInstance Win32_VideoController -ErrorAction Stop)
} catch {
    throw "Could not verify an AMD GPU before installing ROCm. Check Windows Device Manager and retry. $($_.Exception.Message)"
}
function Get-QproNormalizedGpuName($gpu) {
    $name = ([regex]::Replace([regex]::Replace($gpu.Name, '(?i)\((?:TM|R)\)', ' '), '\s+', ' ')).Trim()
    $name = $name -replace '(?i)^AMD\s+', ''
    return ($name -replace '(?i)^RX\s+', 'Radeon RX ')
}
$qproExperimental = -not $UseLegacyRocm
if ($qproExperimental) {
    $qproAmdGpu = $qproVideoControllers | Where-Object {
        $_.PNPDeviceID -match 'VEN_1002' -and $qproStableRadeonNames -contains (Get-QproNormalizedGpuName $_)
    } | Select-Object -First 1
    if ($null -eq $qproAmdGpu) {
        $qproAmdGpu = $qproVideoControllers | Where-Object {
            $_.PNPDeviceID -match 'VEN_1002' -and $qproRocm10Targets.ContainsKey((Get-QproNormalizedGpuName $_))
        } | Select-Object -First 1
    }
} else {
    $qproAmdGpu = $qproVideoControllers | Where-Object {
        $_.PNPDeviceID -match 'VEN_1002' -and $qproStableRadeonNames -contains (Get-QproNormalizedGpuName $_)
    } | Select-Object -First 1
}
if ($null -eq $qproAmdGpu) {
    $qproDetectedNames = ($qproVideoControllers | ForEach-Object { $_.Name }) -join ', '
    if ($UseLegacyRocm) {
        throw "No Radeon on AMD's ROCm 7.2.1 Windows card list was found ($qproDetectedNames). Retry without -UseLegacyRocm for the latest ROCm 10.0 device package."
    }
    throw "No eligible discrete Radeon GPU was found ($qproDetectedNames). Ryzen integrated graphics cannot run Qpro ROCm. Qpro supports selected RX 6000, 7000 and 9000 models with an exact ROCm device package; use Install runtime for NVIDIA CUDA or CPU."
}
$qproGfxTarget = if ($qproExperimental) { $qproRocm10Targets[(Get-QproNormalizedGpuName $qproAmdGpu)] } else { $null }
$qproRocmVersion = if ($qproExperimental) { '10.0.0' } else { '7.2.1' }
$qproRocmEnvName = if ($qproExperimental) { '.venv-rocm-experimental' } else { '.venv-rocm' }
$qproRocmEnv = Join-Path $QproRoot $qproRocmEnvName
$qproRocmPython = Join-Path $qproRocmEnv 'Scripts\python.exe'
$qproReadyMarker = Join-Path $qproRocmEnv 'qpro-rocm-ready.json'
$qproPreviousReady = $null
if (Test-Path -LiteralPath $qproReadyMarker -PathType Leaf) {
    try { $qproPreviousReady = Get-Content -LiteralPath $qproReadyMarker -Raw | ConvertFrom-Json }
    catch { Write-Warning 'The previous AMD readiness record is unreadable; setup will verify the environment again.' }
}
if ($qproExperimental) {
    Write-Host "Discrete AMD GPU detected: $($qproAmdGpu.Name) ($qproGfxTarget)."
    Write-Host 'Installing the latest stable ROCm 10.0 GPU-specific packages in a separate Qpro environment. Setup reports ready only after GPU training and inference checks pass.'
    if ((Get-QproNormalizedGpuName $qproAmdGpu) -match '^Radeon RX 6') {
        Write-Warning 'Radeon RX 6000 support on Windows is experimental for Qpro. AMD does not list these RX gaming cards in its ROCm 10.0 Windows compatibility matrix.'
    }
    if ($qproGfxTarget -in @('gfx1031', 'gfx1032')) {
        Write-Warning "AMD's ROCm 10.0 Windows install guide does not list $qproGfxTarget explicitly. Setup will try AMD's stable device package, then require Qpro GPU checks. If that package is unavailable, use the CPU runtime."
    }
    if ([Environment]::OSVersion.Version.Build -lt 26200) {
        Write-Warning 'AMD validates ROCm 10.0 on Windows 11 25H2. This Windows build is outside that validation; the GPU checks may fail.'
    }
} else {
    Write-Host "Legacy ROCm 7.2.1 explicitly selected for supported discrete AMD GPU: $($qproAmdGpu.Name)"
}

. (Join-Path $QproRoot 'runtime-python.ps1')
$qproBasePython = if (Test-QproPython312 $qproPrivatePython) {
    $qproPrivatePython
} elseif (Test-Path -LiteralPath (Join-Path $qproSharedRuntime 'runtime-ready.json')) {
    Get-QproVenvBasePython312 (Join-Path $qproSharedRuntime '.venv')
} else { $null }
if ([string]::IsNullOrWhiteSpace($qproBasePython)) {
    throw "Open QproFaceTracking.exe and complete PC runtime setup first. The AMD installer needs its Python 3.12 base, which may be an existing installation or Qpro's private copy."
}
if (-not (Test-Path -LiteralPath $qproRequirements)) {
    throw "The QproFaceTracking release was not found: $QproRoot"
}
$env:PYTHONPATH = $QproRoot
if ($qproExperimental) {
    Remove-Item Env:ROCM_SDK_TARGET_FAMILY -ErrorAction SilentlyContinue
    $env:QPRO_ROCM_EXPECTED_GFX_TARGET = $qproGfxTarget
    $env:QPRO_ROCM_INSTALL_SMOKE_TEST = '1'
} else {
    # The pinned 7.2.1 wheels provide the "custom" target family. Selecting it
    # avoids their offload-arch trampoline on install paths containing spaces.
    $env:ROCM_SDK_TARGET_FAMILY = 'custom'
    Remove-Item Env:QPRO_ROCM_EXPECTED_GFX_TARGET -ErrorAction SilentlyContinue
    Remove-Item Env:QPRO_ROCM_INSTALL_SMOKE_TEST -ErrorAction SilentlyContinue
}
$qproSiteCustomize = Join-Path $qproRocmEnv 'Lib\site-packages\sitecustomize.py'
if (Test-Path -LiteralPath $qproSiteCustomize) {
    throw "Remove the existing Python startup customization before continuing: $qproSiteCustomize"
}

& $qproBasePython -c 'import sys; assert sys.version_info[:2] == (3, 12), sys.version'
if ($LASTEXITCODE -ne 0) { throw 'QproFaceTracking requires Python 3.12 for these AMD wheels.' }

# A failed installation must not leave a stale readiness claim behind. Keep
# the previous record long enough to recognize a different gfx target.
if (Test-Path -LiteralPath $qproReadyMarker) {
    Remove-Item -LiteralPath $qproReadyMarker -Force
}

$qproRecreateForBuild = $qproExperimental -and $null -ne $qproPreviousReady -and (
    $qproPreviousReady.gfxTarget -ne $qproGfxTarget -or
    $qproPreviousReady.rocmVersion -ne '10.0.0' -or
    $qproPreviousReady.supportTier -ne 'experimental-rocm-10'
)
if (-not (Test-QproPython312 $qproRocmPython) -or $qproRecreateForBuild) {
    if (Test-Path -LiteralPath $qproRocmEnv) {
        if ($qproRecreateForBuild) {
            Write-Host "Recreating the ROCm 10.0 environment for $qproGfxTarget."
        } else {
            Write-Host 'Repairing an AMD ROCm environment whose Python base is no longer usable.'
        }
        $qproExpectedEnv = [System.IO.Path]::Combine([System.IO.Path]::GetFullPath($QproRoot).TrimEnd('\'), $qproRocmEnvName)
        if (-not [System.IO.Path]::GetFullPath($qproRocmEnv).Equals($qproExpectedEnv, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a path outside the Qpro release: $qproRocmEnv"
        }
        Remove-Item -LiteralPath $qproRocmEnv -Recurse -Force
    }
    & $qproBasePython -m venv $qproRocmEnv
    if ($LASTEXITCODE -ne 0) { throw 'Creating the separate ROCm Python environment failed.' }
}

$qproRuntimeReady = $false
$qproSavedErrorActionPreference = $ErrorActionPreference
$qproTorchVersionProbe = if ($qproExperimental) {
    # The ROCm release and HIP compiler build have separate version numbers.
    "import cv2,numpy,torch,torchvision,torchaudio; from qpro_gpu import is_rocm_10_torch_build,require_rocm_device_name; assert is_rocm_10_torch_build(torch), (torch.__version__,getattr(torch.version,'rocm',None),torch.version.hip); require_rocm_device_name(torch)"
} else {
    "import cv2,numpy,torch; from qpro_gpu import is_rocm_721_torch_build,require_rocm_device_name; assert is_rocm_721_torch_build(torch), (torch.__version__,torch.version.hip); require_rocm_device_name(torch)"
}
try {
    # Windows PowerShell 5.1 can promote Python's expected import traceback on
    # stderr into a terminating NativeCommandError when preference is Stop.
    $ErrorActionPreference = 'Continue'
    Write-Host 'Checking existing ROCm packages and the selected discrete GPU...'
    & $qproRocmPython -c $qproTorchVersionProbe > $null 2>&1
    $qproRuntimeReady = $LASTEXITCODE -eq 0
    if ($qproExperimental -and $null -ne $qproPreviousReady -and ($qproPreviousReady.gfxTarget -ne $qproGfxTarget -or $qproPreviousReady.rocmVersion -ne '10.0.0' -or $qproPreviousReady.supportTier -ne 'experimental-rocm-10')) {
        $qproRuntimeReady = $false
        Write-Host "Configuring the ROCm 10.0 environment for $qproGfxTarget."
    }
} finally {
    $ErrorActionPreference = $qproSavedErrorActionPreference
}

if (-not $qproRuntimeReady) {
    & $qproRocmPython -m pip install --disable-pip-version-check --upgrade pip
    if ($LASTEXITCODE -ne 0) { throw 'Updating pip in the ROCm environment failed.' }

    if ($qproExperimental) {
        # AMD's ROCm 10.0 Windows index supplies the GPU-specific device kernels
        # through PyTorch extras. Keep these wheels away from the 7.2.1 venv.
        $qproRocm10Packages = @(
            "torch[device-$qproGfxTarget]==2.13.0+rocm10.0.0",
            "torchvision[device-$qproGfxTarget]==0.28.0+rocm10.0.0",
            'torchaudio==2.11.0.2+rocm10.0.0'
        )
        Write-Host "Downloading latest stable ROCm 10.0 $qproGfxTarget PyTorch packages; this may take several gigabytes."
        & $qproRocmPython -m pip install --disable-pip-version-check --no-cache-dir --index-url 'https://stable.repo.amd.com/rocm/whl-next/' @qproRocm10Packages
        if ($LASTEXITCODE -ne 0) { throw "Installing AMD ROCm 10.0 $qproGfxTarget PyTorch packages failed. This device package may not be published for your card; Qpro has not changed the older ROCm environment." }
    } else {
        $qproAmdSdkPackages = @(
            'https://repo.radeon.com/rocm/windows/rocm-rel-7.2.1/rocm_sdk_core-7.2.1-py3-none-win_amd64.whl',
            'https://repo.radeon.com/rocm/windows/rocm-rel-7.2.1/rocm_sdk_devel-7.2.1-py3-none-win_amd64.whl',
            'https://repo.radeon.com/rocm/windows/rocm-rel-7.2.1/rocm_sdk_libraries_custom-7.2.1-py3-none-win_amd64.whl',
            'https://repo.radeon.com/rocm/windows/rocm-rel-7.2.1/rocm-7.2.1.tar.gz'
        )
        & $qproRocmPython -m pip install --disable-pip-version-check --no-cache-dir @qproAmdSdkPackages
        if ($LASTEXITCODE -ne 0) { throw 'Installing AMD ROCm 7.2.1 components failed.' }

        $qproAmdTorchPackages = @(
            'https://repo.radeon.com/rocm/windows/rocm-rel-7.2.1/torch-2.9.1%2Brocm7.2.1-cp312-cp312-win_amd64.whl',
            'https://repo.radeon.com/rocm/windows/rocm-rel-7.2.1/torchaudio-2.9.1%2Brocm7.2.1-cp312-cp312-win_amd64.whl',
            'https://repo.radeon.com/rocm/windows/rocm-rel-7.2.1/torchvision-0.24.1%2Brocm7.2.1-cp312-cp312-win_amd64.whl'
        )
        & $qproRocmPython -m pip install --disable-pip-version-check --no-cache-dir @qproAmdTorchPackages
        if ($LASTEXITCODE -ne 0) { throw 'Installing AMD ROCm PyTorch 2.9.1 failed.' }
    }

    & $qproRocmPython -m pip install --disable-pip-version-check -r $qproRequirements
    if ($LASTEXITCODE -ne 0) { throw 'Installing QproFaceTracking Python requirements failed.' }
}

if ($qproRuntimeReady) {
    Write-Host 'Required ROCm packages are already installed. Running GPU training and inference checks.'
}

$qproFix = "if torch.version.hip:`r`n    # Windows MIOpen HIPRTC cannot compile these tongue-model BatchNorm kernels.`r`n    torch.backends.cudnn.enabled = False`r`n"
foreach ($qproScript in @('train_tongue_model.py', 'tongue_model_preview.py')) {
    $qproScriptPath = Join-Path $QproRoot $qproScript
    if (-not (Test-Path -LiteralPath $qproScriptPath)) { throw "Missing Qpro script: $qproScriptPath" }
    $qproText = [System.IO.File]::ReadAllText($qproScriptPath)
    if ($qproText.Contains('torch.backends.cudnn.enabled = False')) { continue }
    $qproImport = [regex]::Match($qproText, '(?m)^import torch\r?\n')
    if (-not $qproImport.Success) { throw "Cannot locate the torch import in $qproScriptPath" }
    $qproNewline = if ($qproImport.Value.EndsWith("`r`n")) { "`r`n" } else { "`n" }
    $qproInsertion = "$qproNewline" + $qproFix.Replace("`r`n", $qproNewline)
    $qproBackup = "$qproScriptPath.pre-rocm.bak"
    if (-not (Test-Path -LiteralPath $qproBackup)) { Copy-Item -LiteralPath $qproScriptPath -Destination $qproBackup }
    $qproUpdated = $qproText.Insert($qproImport.Index + $qproImport.Length, $qproInsertion)
    [System.IO.File]::WriteAllText($qproScriptPath, $qproUpdated, [System.Text.UTF8Encoding]::new($false))
}

& $qproRocmPython -c "import cv2,numpy,torch; from qpro_gpu import require_rocm_device_name; print('PyTorch:',torch.__version__,flush=True); print('ROCm release:',getattr(torch.version,'rocm',None),flush=True); print('HIP build:',torch.version.hip,flush=True); print('GPU available:',torch.cuda.is_available(),flush=True); device=require_rocm_device_name(torch); print('GPU:',torch.cuda.get_device_name(int(device.split(':')[1]))); print('Device:',device)"
if ($LASTEXITCODE -ne 0) { throw 'ROCm installed, but PyTorch did not detect a supported discrete Radeon GPU. Integrated graphics are not selected.' }

& $qproRocmPython -c "import sys,torch; from qpro_gpu import require_rocm_device_name; from train_tongue_model import create_model; device=require_rocm_device_name(torch); assert not torch.backends.cudnn.enabled; c=torch.load(sys.argv[1],map_location='cpu',weights_only=False); m=create_model(c['architecture'],list(c['targetNames'])).to(device); x=torch.rand(2,2,c['imageSize'],c['imageSize'],device=device); m(x).float().square().mean().backward(); torch.cuda.synchronize(device); print('GPU training smoke test passed on',device)" $qproGateModel
if ($LASTEXITCODE -ne 0) { throw 'The Qpro tongue model failed a GPU training step.' }

& $qproRocmPython -c "import sys,numpy as np,torch; from qpro_gpu import require_rocm_device_name; from tongue_model_preview import LiveTongueModelPreview; assert not torch.backends.cudnn.enabled; p=LiveTongueModelPreview(sys.argv[1],direction_checkpoint_path=sys.argv[2]); assert str(p.device)==require_rocm_device_name(torch); p.predict(np.zeros((400,800),dtype=np.uint8),None,[]); torch.cuda.synchronize(p.device); print('GPU inference smoke test passed on',p.device)" $qproGateModel $qproDirectionModel
if ($LASTEXITCODE -ne 0) { throw 'The Qpro tongue model failed a GPU inference step.' }

Remove-Item Env:QPRO_ROCM_INSTALL_SMOKE_TEST -ErrorAction SilentlyContinue
Remove-Item Env:QPRO_ROCM_EXPECTED_GFX_TARGET -ErrorAction SilentlyContinue

@{
    schema = 1
    rocmVersion = $qproRocmVersion
    supportTier = if ($qproExperimental) { 'experimental-rocm-10' } else { 'amd-windows-7.2.1' }
    gfxTarget = $qproGfxTarget
    python = $qproRocmPython
    gpu = $qproAmdGpu.Name
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json | Set-Content -LiteralPath $qproReadyMarker -Encoding UTF8
try {
    & $qproRocmPython -c "import torch; from qpro_gpu import require_rocm_device_name; print('Final readiness check:', require_rocm_device_name(torch))"
    if ($LASTEXITCODE -ne 0) { throw 'The ROCm environment failed its final readiness check.' }
} catch {
    Remove-Item -LiteralPath $qproReadyMarker -Force -ErrorAction SilentlyContinue
    throw
}
Write-Host "ROCm runtime ready: $qproRocmPython"
Write-Host 'Open QproFaceTracking.exe. Tongue tracking and training will select this ROCm runtime automatically.'
