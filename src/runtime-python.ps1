# Base-interpreter discovery only. Qpro packages always install into their own venvs.
function Test-QproPython312([string]$Python) {
    if ([string]::IsNullOrWhiteSpace($Python) -or -not (Test-Path -LiteralPath $Python -PathType Leaf)) { return $false }
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        # Windows PowerShell 5.1 strips embedded double quotes from native
        # arguments. Keep Python string literals single-quoted in -c code.
        $check = "import sys,venv,platform; assert sys.version_info[:2] == (3,12) and sys.maxsize > 2**32 and platform.machine().lower() in ('amd64','x86_64'); print('QPRO_PYTHON312_OK')"
        $output = @(& $Python -c $check 2>$null)
        return $LASTEXITCODE -eq 0 -and $output.Count -gt 0 -and $output[-1].ToString().Trim() -eq 'QPRO_PYTHON312_OK'
    }
    catch { return $false }
    finally { $ErrorActionPreference = $previousPreference }
}

function Find-QproExistingPython312 {
    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($env:QPRO_BASE_PYTHON)) {
        $candidates.Add($env:QPRO_BASE_PYTHON)
    }
    $candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'))

    foreach ($registryPath in @(
        'Registry::HKEY_CURRENT_USER\Software\Python\PythonCore\3.12\InstallPath',
        'Registry::HKEY_LOCAL_MACHINE\Software\Python\PythonCore\3.12\InstallPath',
        'Registry::HKEY_LOCAL_MACHINE\Software\WOW6432Node\Python\PythonCore\3.12\InstallPath'
    )) {
        $key = Get-Item -LiteralPath $registryPath -ErrorAction SilentlyContinue
        if ($null -eq $key) { continue }
        $executable = $key.GetValue('ExecutablePath')
        if (-not [string]::IsNullOrWhiteSpace($executable)) { $candidates.Add($executable) }
        $pythonInstallDir = $key.GetValue('')
        if (-not [string]::IsNullOrWhiteSpace($pythonInstallDir)) { $candidates.Add((Join-Path $pythonInstallDir 'python.exe')) }
    }

    $pathPython = Get-Command python.exe -ErrorAction SilentlyContinue
    if ($null -ne $pathPython -and $pathPython.Source -notmatch '[\\/]WindowsApps[\\/]') {
        $candidates.Add($pathPython.Source)
    }
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-QproPython312 $candidate) { return [System.IO.Path]::GetFullPath($candidate) }
    }

    $launcher = Get-Command py.exe -ErrorAction SilentlyContinue
    if ($null -eq $launcher) { return $null }
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $launcher.Source -3.12 -c 'import sys,platform; assert sys.version_info[:2] == (3,12) and sys.maxsize > 2**32 and platform.machine().lower() in ("amd64","x86_64"); print(sys.executable)' 2>$null)
        if ($LASTEXITCODE -eq 0 -and $output.Count -gt 0) {
            $candidate = $output[-1].ToString().Trim()
            if (Test-QproPython312 $candidate) { return [System.IO.Path]::GetFullPath($candidate) }
        }
    }
    finally { $ErrorActionPreference = $previousPreference }
    return $null
}

function Get-QproVenvBasePython312([string]$VenvRoot) {
    $config = Join-Path $VenvRoot 'pyvenv.cfg'
    if (-not (Test-Path -LiteralPath $config -PathType Leaf)) { return $null }
    foreach ($line in [System.IO.File]::ReadAllLines($config)) {
        $match = [regex]::Match($line, '^home\s*=\s*(.+)$')
        if (-not $match.Success) { continue }
        $candidate = Join-Path $match.Groups[1].Value.Trim() 'python.exe'
        if (Test-QproPython312 $candidate) { return [System.IO.Path]::GetFullPath($candidate) }
    }
    return $null
}

function Test-QproPython312Registration {
    if (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe')) { return $true }
    foreach ($registryPath in @(
        'Registry::HKEY_CURRENT_USER\Software\Python\PythonCore\3.12\InstallPath',
        'Registry::HKEY_LOCAL_MACHINE\Software\Python\PythonCore\3.12\InstallPath',
        'Registry::HKEY_LOCAL_MACHINE\Software\WOW6432Node\Python\PythonCore\3.12\InstallPath'
    )) {
        if (Test-Path -LiteralPath $registryPath) { return $true }
    }
    return $false
}
