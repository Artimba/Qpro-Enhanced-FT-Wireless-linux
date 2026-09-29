param()

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$sharedRoot = Join-Path $env:LOCALAPPDATA "QproFaceTracking\runtime"
$venvRoot = Join-Path $sharedRoot ".venv"
$venvPython = Join-Path $venvRoot "Scripts\python.exe"
$privatePythonRoot = Join-Path $sharedRoot "python-3.12.10"
$privatePython = Join-Path $privatePythonRoot "python.exe"
$bundledPythonArchive = Join-Path $root "python-runtime\python.3.12.10.nupkg"
$bundledPythonSha256 = "0eb85c2dfccccf1b17352de4c397f69194035b7d37149eacc16f1147d93de3b8"
$readyMarker = Join-Path $sharedRoot "runtime-ready.json"
$requirements = Join-Path $root "requirements-runtime.txt"
$torchRequirement = "torch>=2.7,<3"
$cudaIndex = "https://download.pytorch.org/whl/cu128"
$cpuIndex = "https://download.pytorch.org/whl/cpu"
Write-Host 'PC runtime setup script started. Checking bundled files and Windows environment...'
. (Join-Path $root 'runtime-python.ps1')

function Assert-QproManagedRuntimePath([string]$Candidate, [string]$ExpectedName) {
    $managedRoot = [System.IO.Path]::GetFullPath($sharedRoot).TrimEnd('\')
    $expected = [System.IO.Path]::Combine($managedRoot, $ExpectedName)
    if (-not [System.IO.Path]::GetFullPath($Candidate).Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside Qpro's managed runtime: $Candidate"
    }
}

function Install-QproPrivatePython {
    if (-not (Test-Path -LiteralPath $bundledPythonArchive -PathType Leaf)) {
        throw 'The bundled private Python archive is missing. Re-extract the complete release ZIP.'
    }
    Write-Host 'Checking the bundled Python archive before extraction...'
    $actualHash = (Get-FileHash -LiteralPath $bundledPythonArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $bundledPythonSha256) {
        throw 'The bundled private Python archive failed its integrity check. Re-extract or download the release again.'
    }

    # CPython's NuGet archive contains an unregistered Python tree under tools/.
    # Extracting it avoids the Windows installer's Modify mode entirely.
    $stagingRoot = Join-Path $sharedRoot ('python-stage-' + [guid]::NewGuid().ToString('N'))
    Write-Host "Preparing Qpro's private Python 3.12 runtime. Existing Python installations are untouched."
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        Write-Host 'Extracting private Python. This may take longer while Windows Security scans the files...'
        [System.IO.Compression.ZipFile]::ExtractToDirectory($bundledPythonArchive, $stagingRoot)
        $stagedPythonRoot = Join-Path $stagingRoot 'tools'
        $stagedPython = Join-Path $stagedPythonRoot 'python.exe'
        if (-not (Test-QproPython312 $stagedPython)) {
            throw 'Qpro private Python could not start. If Activity shows a missing DLL or VCRUNTIME error, install or repair Microsoft Visual C++ Redistributable x64: https://aka.ms/vc14/vc_redist.x64.exe'
        }
        if (Test-Path -LiteralPath $privatePythonRoot) {
            Write-Host 'Replacing an incomplete Qpro private Python copy.'
            Assert-QproManagedRuntimePath $privatePythonRoot 'python-3.12.10'
            Remove-Item -LiteralPath $privatePythonRoot -Recurse -Force
        }
        Move-Item -LiteralPath $stagedPythonRoot -Destination $privatePythonRoot
    }
    finally {
        if (Test-Path -LiteralPath $stagingRoot) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
    }
    if (-not (Test-QproPython312 $privatePython)) {
        throw "Qpro's private Python did not start after extraction: $privatePython. For missing DLL or VCRUNTIME errors, install or repair Microsoft Visual C++ Redistributable x64: https://aka.ms/vc14/vc_redist.x64.exe"
    }
}

if (-not (Test-Path -LiteralPath $requirements)) {
    throw "requirements-runtime.txt is missing. Reinstall the release package."
}

function Test-PythonCommand([string]$Python, [string]$Code) {
    $previousPreference = $ErrorActionPreference
    try {
        # Import failures are expected while repairing a new/partial environment.
        # Do not let stderr become a terminating NativeCommandError.
        $ErrorActionPreference = "Continue"
        & $Python -c $Code *> $null
        return $LASTEXITCODE -eq 0
    }
    catch { return $false }
    finally {
        $ErrorActionPreference = $previousPreference
    }
}

function Write-ReadyMarker([string]$Python) {
    $payload = @{
        format = "qpro-runtime-ready-v1"
        python = $Python
        completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    } | ConvertTo-Json
    Set-Content -LiteralPath $readyMarker -Value $payload -Encoding UTF8
}

function Reset-QproTrackingEnvironment {
    New-Item -ItemType Directory -Force -Path $sharedRoot | Out-Null
    if (-not (Test-QproPython312 $privatePython)) { Install-QproPrivatePython }
    if (Test-Path -LiteralPath $venvRoot) {
        Write-Host "Rebuilding Qpro's incomplete tracking environment; other Python installations and Qpro settings are untouched."
        Assert-QproManagedRuntimePath $venvRoot '.venv'
        Remove-Item -LiteralPath $venvRoot -Recurse -Force
    }
    Write-Host "Creating Qpro's separate tracking environment from its private Python."
    & $privatePython -m venv $venvRoot
    if ($LASTEXITCODE -ne 0 -or -not (Test-QproPython312 $venvPython)) {
        throw 'Creating the local Python environment failed. Check Activity for the specific error. For missing DLL or VCRUNTIME errors, install or repair Microsoft Visual C++ Redistributable x64: https://aka.ms/vc14/vc_redist.x64.exe'
    }
}

function Get-QproRuntimeImportDiagnostic([string]$Python) {
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $details = @(& $Python -c "import cv2,numpy,torch; assert hasattr(cv2,'namedWindow')" 2>&1 |
            ForEach-Object { $_.ToString() })
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = ($details -join "`n") }
    }
    catch {
        return [pscustomobject]@{ ExitCode = -1; Text = $_.Exception.Message }
    }
    finally { $ErrorActionPreference = $previousPreference }
}

function Get-QproNvidiaNames {
    $command = Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue
    if ($null -eq $command) { return '' }
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo.FileName = $command.Source
    $process.StartInfo.Arguments = '--query-gpu=name --format=csv,noheader'
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    try {
        if (-not $process.Start()) { return '' }
        if (-not $process.WaitForExit(8000)) {
            $process.Kill()
            Write-Warning 'NVIDIA GPU detection took too long. Continuing with CPU setup; update the NVIDIA driver and rerun setup for CUDA.'
            return ''
        }
        if ($process.ExitCode -ne 0) { return '' }
        return $process.StandardOutput.ReadToEnd().Trim()
    }
    catch {
        Write-Warning "NVIDIA GPU detection failed: $($_.Exception.Message). Continuing with CPU setup."
        return ''
    }
    finally { $process.Dispose() }
}

Write-Host 'Checking NVIDIA driver and GPU (up to 8 seconds)...'
$nvidiaName = Get-QproNvidiaNames
$nvidiaDetected = -not [string]::IsNullOrWhiteSpace($nvidiaName)
if ($nvidiaDetected) { Write-Host "NVIDIA GPU detected: $nvidiaName" }

if (-not [string]::IsNullOrWhiteSpace($env:QPRO_PYTHON) -and (Test-Path -LiteralPath $env:QPRO_PYTHON)) {
    Write-Host 'Checking the Python runtime explicitly selected by QPRO_PYTHON...'
    if (Test-PythonCommand $env:QPRO_PYTHON "import cv2,numpy,torch; assert hasattr(cv2,'namedWindow')") {
        & $env:QPRO_PYTHON -c "import cv2,numpy,torch; print('Existing runtime ready:', torch.__version__, 'CUDA:', torch.cuda.is_available())"
        Write-Host "An explicitly configured QPRO_PYTHON runtime is ready. Training will automatically use CUDA when that runtime exposes it, otherwise CPU."
        exit 0
    }
}

$newEnvironment = $false
if (-not (Test-QproPython312 $venvPython)) {
    Write-Host 'The shared Qpro Python environment is missing or incomplete; creating a private one.'
    Reset-QproTrackingEnvironment
    $newEnvironment = $true
}
if (Test-Path -LiteralPath $venvPython) {
    Write-Host 'Checking OpenCV, NumPy, and PyTorch in the shared Qpro environment. The first import can take a while...'
    $existingRuntimeReady = Test-PythonCommand $venvPython "import cv2,numpy,torch; assert hasattr(cv2,'namedWindow')"
    $existingCudaReady = $existingRuntimeReady -and (Test-PythonCommand $venvPython "import torch,sys; sys.exit(0 if torch.cuda.is_available() else 1)")
    if ($existingRuntimeReady -and (-not $nvidiaDetected -or $existingCudaReady)) {
        & $venvPython -c "import cv2,numpy,torch; print('Existing shared runtime ready:', torch.__version__, 'CUDA:', torch.cuda.is_available())"
        Write-ReadyMarker $venvPython
        Write-Host "No runtime reinstall was needed."
        exit 0
    }
    if ($existingRuntimeReady -and $nvidiaDetected -and -not $existingCudaReady) {
        Write-Host "The existing runtime is CPU-only even though an NVIDIA GPU is present. Repairing its PyTorch installation."
    }
    if (-not $existingRuntimeReady -and -not $newEnvironment) {
        $diagnostic = Get-QproRuntimeImportDiagnostic $venvPython
        Write-Host "The existing Qpro runtime failed its import check (exit $($diagnostic.ExitCode))."
        if (-not [string]::IsNullOrWhiteSpace($diagnostic.Text)) { Write-Host $diagnostic.Text }
        Reset-QproTrackingEnvironment
    }
}

if (Test-Path -LiteralPath $readyMarker) { Remove-Item -LiteralPath $readyMarker -Force }
Write-Host "Installing the shared tracking runtime. PyTorch is large; this may take several minutes."
& $venvPython -m pip install --disable-pip-version-check --no-input --upgrade pip
if ($LASTEXITCODE -ne 0) {
    throw 'Updating pip failed. Read the preceding Activity error. If it mentions a missing DLL, VCRUNTIME, or MSVCP140, install or repair Microsoft Visual C++ Redistributable x64: https://aka.ms/vc14/vc_redist.x64.exe'
}
Write-Host 'Installing OpenCV, NumPy, and the other tracking requirements...'
& $venvPython -m pip install --disable-pip-version-check --no-input -r $requirements
if ($LASTEXITCODE -ne 0) {
    throw 'Installing the tracking runtime failed. Read the preceding Activity error. For missing DLL, VCRUNTIME, or MSVCP140 errors, install or repair Microsoft Visual C++ Redistributable x64: https://aka.ms/vc14/vc_redist.x64.exe'
}

if ($nvidiaDetected) {
    Write-Host "Installing the official CUDA 12.8 PyTorch wheel for $nvidiaName."
    $cudaInstallOptions = @()
    if ($existingRuntimeReady -and -not $existingCudaReady) {
        # A CPU wheel satisfies torch>=2.7,<3, even when pip's index points at
        # CUDA. Force replacement only for this repair path.
        $cudaInstallOptions = @('--force-reinstall')
        Write-Host 'Replacing the CPU-only PyTorch wheel with the CUDA build.'
    }
    & $venvPython -m pip install --disable-pip-version-check --no-input --upgrade @cudaInstallOptions $torchRequirement --index-url $cudaIndex
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "The CUDA PyTorch download failed. Installing the CPU build so tracking and training remain usable."
        & $venvPython -m pip install --disable-pip-version-check --no-input --upgrade $torchRequirement --index-url $cpuIndex
        if ($LASTEXITCODE -ne 0) { throw "Installing both CUDA and CPU PyTorch builds failed. Check the internet connection and run setup again." }
    }
}
else {
    Write-Host "No NVIDIA driver/GPU was detected. Installing the official CPU PyTorch wheel."
    & $venvPython -m pip install --disable-pip-version-check --no-input --upgrade $torchRequirement --index-url $cpuIndex
    if ($LASTEXITCODE -ne 0) { throw "Installing the CPU PyTorch build failed." }
}

Write-Host 'Verifying that the installed OpenCV, NumPy, and PyTorch libraries load successfully...'
if (-not (Test-PythonCommand $venvPython "import cv2,numpy,torch; assert hasattr(cv2,'namedWindow')")) {
    $diagnostic = Get-QproRuntimeImportDiagnostic $venvPython
    if (-not [string]::IsNullOrWhiteSpace($diagnostic.Text)) { Write-Host $diagnostic.Text }
    if ($diagnostic.Text -match '(?i)DLL load failed|VCRUNTIME|MSVCP140|Microsoft Visual C\+\+' -or
        $diagnostic.ExitCode -in @(-1073741515, 3221225781)) {
        throw 'A Windows native dependency could not load. Install or repair the latest Microsoft Visual C++ Redistributable x64 from https://aka.ms/vc14/vc_redist.x64.exe, then rerun PC runtime setup. Qpro settings and captures can stay in place.'
    }
    throw "The installed runtime failed its final import check (exit $($diagnostic.ExitCode)). Send the complete Activity log; there is no need to delete Qpro settings or captures."
}
& $venvPython -c "import cv2,numpy,torch; print('Runtime ready:', torch.__version__, 'CUDA:', torch.cuda.is_available())"

if ($nvidiaDetected) {
    if (-not (Test-PythonCommand $venvPython "import torch,sys; sys.exit(0 if torch.cuda.is_available() else 1)")) {
        Write-Warning "NVIDIA hardware was detected, but PyTorch still cannot initialize CUDA. Update/reinstall the NVIDIA display driver, then run this setup again. CPU training fallback remains available meanwhile."
    }
}

Write-ReadyMarker $venvPython
Write-Host "PC runtime setup complete. You can close this window and press Refresh in QproFaceTracking."
