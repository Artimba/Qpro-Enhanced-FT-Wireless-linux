param(
    [switch]$Rebuild,
    [ValidateSet("VirtualDesktop", "SteamLink")][string]$TrackingSource,
    [string]$VrcftInstallDir = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$customLibs = Join-Path $env:APPDATA "VRCFaceTracking\CustomLibs"
$research = Join-Path $root "research"
$legacyId = "7f9be083-a4f1-4e30-b28a-8e6ec878d583"
$legacyPath = Join-Path $customLibs $legacyId
$backupPath = Join-Path $research "vrcft-legacy-registry-module-backup"
$officialId = "91a90618-b020-4064-8832-809b2ca2b3bc"
$officialPath = Join-Path $customLibs $officialId
$officialBackup = Join-Path $research "vrcft-official-virtual-desktop-backup"
$virtualDesktopModule = Join-Path $customLibs "000-Qpro.VirtualDesktop.dll"
$steamLinkModule = Join-Path $customLibs "000-Qpro.SteamLink.dll"
$oldCombinedModule = Join-Path $customLibs "000-Qpro.IndependentGaze.dll"
$sourcePath = Join-Path $env:LOCALAPPDATA "QproFaceTracking\config\tracking-source.txt"

if (-not $TrackingSource) {
    $TrackingSource = if ((Test-Path -LiteralPath $sourcePath -PathType Leaf) -and
        (Get-Content -LiteralPath $sourcePath -Raw).Trim() -eq "steam-link") { "SteamLink" } else { "VirtualDesktop" }
}
$sourceName = if ($TrackingSource -eq "SteamLink") { "Steam Link" } else { "Virtual Desktop" }
$sourceToken = if ($TrackingSource -eq "SteamLink") { "steam-link" } else { "virtual-desktop" }
$destination = if ($TrackingSource -eq "SteamLink") { $steamLinkModule } else { $virtualDesktopModule }
$qproModulePaths = @($virtualDesktopModule, $steamLinkModule, $oldCombinedModule)

if (Get-Process -Name "VRCFaceTracking", "VRCFaceTracking.ModuleProcess", "ModuleProcess" -ErrorAction SilentlyContinue) {
    throw "Close VRCFaceTracking and wait for its ModuleProcess helper to exit before installing the Qpro $sourceName module. Qpro will not close them automatically."
}

function Assert-DirectChild([string]$Path, [string]$Parent) {
    $full = [System.IO.Path]::GetFullPath($Path)
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\')
    if (-not [System.IO.Path]::GetDirectoryName($full).Equals($parentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Module path is outside its expected directory: $full"
    }
}

function Assert-QproModule([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Assert-DirectChild $Path $customLibs
    $item = Get-Item -LiteralPath $Path
    if ($item.PSIsContainer -or ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The Qpro module path is not a regular DLL and was left untouched: $Path"
    }
    try { $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($Path).Name }
    catch { throw "The file at $Path is not a readable Qpro module DLL. It was left untouched." }
    if ($assemblyName -ne "Qpro.GazeBridge") {
        throw "The file at $Path is not the Qpro module (assembly: $assemblyName). It was left untouched."
    }
}

function Resolve-VrcftInstallDir([string]$ExplicitDir) {
    $requiredDlls = @("VRCFaceTracking.Core.dll", "VRCFaceTracking.SDK.dll", "Microsoft.Extensions.Logging.Abstractions.dll")
    function Has-RequiredDlls([string]$Directory) {
        (Test-Path -LiteralPath $Directory -PathType Container) -and
            @($requiredDlls | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Directory $_) -PathType Leaf) }).Count -eq 0
    }

    if (-not [string]::IsNullOrWhiteSpace($ExplicitDir)) {
        $resolved = [System.IO.Path]::GetFullPath($ExplicitDir)
        if (Has-RequiredDlls $resolved) { return $resolved }
        throw "VRCFaceTracking DLLs were not found in -VrcftInstallDir $resolved."
    }

    $steamRoots = @(
        [System.IO.Path]::Combine([Environment]::GetFolderPath("ProgramFilesX86"), "Steam"),
        [System.IO.Path]::Combine([Environment]::GetFolderPath("ProgramFiles"), "Steam")
    )
    foreach ($key in @("HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam")) {
        try {
            $settings = Get-ItemProperty -LiteralPath $key -ErrorAction Stop
            foreach ($value in @($settings.SteamPath, $settings.InstallPath)) {
                if (-not [string]::IsNullOrWhiteSpace($value)) { $steamRoots += $value }
            }
        } catch { }
    }
    foreach ($steamRoot in $steamRoots | Sort-Object -Unique) {
        if (-not (Test-Path -LiteralPath $steamRoot -PathType Container)) { continue }
        $libraries = @($steamRoot)
        $libraryFile = Join-Path $steamRoot "steamapps\libraryfolders.vdf"
        if (Test-Path -LiteralPath $libraryFile -PathType Leaf) {
            try {
                $contents = Get-Content -LiteralPath $libraryFile -Raw -ErrorAction Stop
                foreach ($match in [regex]::Matches($contents, '(?m)^\s*"path"\s*"([^"]+)"')) {
                    $libraries += $match.Groups[1].Value.Replace('\\', '\')
                }
            } catch { }
        }
        foreach ($library in $libraries | Sort-Object -Unique) {
            if (-not (Test-Path -LiteralPath $library -PathType Container)) { continue }
            $installName = "VRCFaceTracking"
            $manifestPath = Join-Path $library "steamapps\appmanifest_3329480.acf"
            if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
                try {
                    $manifest = Get-Content -LiteralPath $manifestPath -Raw -ErrorAction Stop
                    if ($manifest -match '(?m)^\s*"installdir"\s*"([^"]+)"') { $installName = $Matches[1] }
                } catch { }
            }
            $candidate = Join-Path $library ("steamapps\common\" + $installName)
            if (Has-RequiredDlls $candidate) { return [System.IO.Path]::GetFullPath($candidate) }
        }
    }
    throw "Could not find the Steam VRCFaceTracking install folder. Pass -VrcftInstallDir with the folder containing VRCFaceTracking.Core.dll and VRCFaceTracking.SDK.dll."
}

# Qpro owns both VRCFaceTracking slots and receives Steam Link OSC on port 9015.
# Detect obvious competing modules before this installer moves or writes files.
function Find-CompetingSteamLinkModules {
    if (-not (Test-Path -LiteralPath $customLibs -PathType Container)) { return }
    $knownLinkFtId = "2a8c8080-2a76-46af-bf76-1da7c0127ef8"
    foreach ($item in Get-ChildItem -LiteralPath $customLibs -Force) {
        if ($item.Name -in @("000-Qpro.VirtualDesktop.dll", "000-Qpro.SteamLink.dll", "000-Qpro.IndependentGaze.dll")) { continue }
        if ($item.Name -match '(?i)steam[ ._-]*link|linkft' -or $item.Name -eq $knownLinkFtId) {
            $item.FullName
            continue
        }
        if (-not $item.PSIsContainer -or
            ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
        $namedDll = Get-ChildItem -LiteralPath $item.FullName -File -Filter "*.dll" -ErrorAction SilentlyContinue |
            Where-Object Name -Match '(?i)steam[ ._-]*link|linkft' |
            Select-Object -First 1
        if ($namedDll) {
            $item.FullName
            continue
        }
        $manifestPath = Join-Path $item.FullName "module.json"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { continue }
        $manifestItem = Get-Item -LiteralPath $manifestPath
        if ($manifestItem.Length -gt 65536 -or
            ($manifestItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -ErrorAction Stop
            $identity = @($manifest.ModuleName, $manifest.DllFileName, $manifest.ModuleId, $manifest.ModulePageUrl, $manifest.DownloadUrl) -join " "
            if ($identity -match '(?i)steam[ ._-]*link|linkft|2a8c8080-2a76-46af-bf76-1da7c0127ef8') {
                $item.FullName
            }
        } catch {
            # An unreadable manifest is not enough to identify this as LinkFT.
        }
    }
}

$competingModules = @(Find-CompetingSteamLinkModules | Sort-Object -Unique)
if ($competingModules.Count -gt 0) {
    throw "Another Steam Link/LinkFT VRCFaceTracking module is present at $($competingModules -join '; '). Qpro needs the OSC port and eye/face slots. Remove that module through VRCFaceTracking, then retry Install $sourceName module. Qpro did not change it."
}

$source = Join-Path $root "vrcft-gaze-bridge\bin\Release\net10.0\Qpro.GazeBridge.dll"
if ($Rebuild -or -not (Test-Path -LiteralPath $source)) {
    $project = Join-Path $root "vrcft-gaze-bridge\Qpro.GazeBridge.csproj"
    if (-not (Test-Path -LiteralPath $project)) {
        throw "The prebuilt combined VRCFT module is missing. Reinstall the release package."
    }
    $resolvedVrcftDir = Resolve-VrcftInstallDir $VrcftInstallDir
    & dotnet build $project -c Release "-p:VrcftInstallDir=$resolvedVrcftDir"
    if ($LASTEXITCODE -ne 0) { throw "Building the VRCFT module failed." }
}
if (-not (Test-Path -LiteralPath $source)) { throw "The Qpro module DLL was not produced." }
try { $sourceAssemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($source).Name }
catch { throw "The packaged Qpro module is not a readable .NET DLL: $source" }
if ($sourceAssemblyName -ne "Qpro.GazeBridge") {
    throw "The packaged module has the wrong assembly identity ($sourceAssemblyName): $source"
}
foreach ($path in $qproModulePaths) { Assert-QproModule $path }

New-Item -ItemType Directory -Force -Path $customLibs, (Split-Path -Parent $sourcePath), $research | Out-Null
foreach ($directory in @($customLibs, (Split-Path -Parent $sourcePath), $research)) {
    if (((Get-Item -LiteralPath $directory).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The module or recovery directory is a link and was left untouched: $directory"
    }
}
$transactionId = [guid]::NewGuid().ToString("N")
$stageModule = Join-Path $customLibs (".qpro-module-" + $transactionId + ".tmp")
$stageToken = Join-Path (Split-Path -Parent $sourcePath) (".qpro-source-" + $transactionId + ".tmp")
$recoveryDirectory = Join-Path $research ("vrcft-qpro-module-switch-backup-" + $transactionId)
$existingQproModules = @($qproModulePaths | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
$configExisted = Test-Path -LiteralPath $sourcePath -PathType Leaf
if ($configExisted -and
    ((Get-Item -LiteralPath $sourcePath).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "The face-tracking source file is a link and was left untouched: $sourcePath"
}
$commitStarted = $false
$movedModules = @()

try {
    Copy-Item -LiteralPath $source -Destination $stageModule
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $stageModule -Algorithm SHA256).Hash -ne $sourceHash) {
        throw "The staged Qpro module failed its hash check."
    }
    [System.IO.File]::WriteAllText($stageToken, $sourceToken, [System.Text.UTF8Encoding]::new($false))
    if ((Get-Content -LiteralPath $stageToken -Raw).Trim() -ne $sourceToken) {
        throw "The staged face-tracking source did not match $sourceName."
    }

    if ($existingQproModules.Count -gt 0 -or $configExisted) {
        New-Item -ItemType Directory -Path $recoveryDirectory | Out-Null
        foreach ($path in $existingQproModules) {
            $recoveryCopy = Join-Path $recoveryDirectory (Split-Path -Leaf $path)
            Assert-DirectChild $recoveryCopy $recoveryDirectory
            Copy-Item -LiteralPath $path -Destination $recoveryCopy
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne
                (Get-FileHash -LiteralPath $recoveryCopy -Algorithm SHA256).Hash) {
                throw "Could not verify the recovery copy of $path."
            }
        }
        if ($configExisted) {
            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $recoveryDirectory "tracking-source.txt")
        }
    }

    foreach ($pair in @(@($legacyPath, $backupPath), @($officialPath, $officialBackup))) {
        $installedPath = $pair[0]
        $savedPath = $pair[1]
        if (-not (Test-Path -LiteralPath $installedPath)) { continue }
        if (Test-Path -LiteralPath $savedPath) {
            if ($installedPath -eq $legacyPath) { throw "Backup path already exists: $savedPath. Move or remove it before retrying." }
            $savedPath = Join-Path $research ("vrcft-official-virtual-desktop-backup-" + $transactionId)
        }
        Assert-DirectChild $installedPath $customLibs
        Assert-DirectChild $savedPath $research
        if (((Get-Item -LiteralPath $installedPath).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "The existing module is a link and was left untouched: $installedPath"
        }
        Move-Item -LiteralPath $installedPath -Destination $savedPath
        $movedModules += @{ Installed = $installedPath; Saved = $savedPath }
        Write-Host "Saved the previous Virtual Desktop module at $savedPath"
    }

    if (Get-Process -Name "VRCFaceTracking", "VRCFaceTracking.ModuleProcess", "ModuleProcess" -ErrorAction SilentlyContinue) {
        throw "VRCFaceTracking or its ModuleProcess helper opened during installation. Close it and retry."
    }
    foreach ($path in $qproModulePaths) {
        Assert-QproModule $path
        if ((Test-Path -LiteralPath $path) -and $path -notin $existingQproModules) {
            throw "A Qpro module appeared during installation and was left untouched: $path. Retry after checking VRCFaceTracking."
        }
    }
    if ((Test-Path -LiteralPath $sourcePath) -and
        ((Get-Item -LiteralPath $sourcePath).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The face-tracking source file became a link and was left untouched: $sourcePath"
    }
    $commitStarted = $true
    foreach ($path in $existingQproModules) { Remove-Item -LiteralPath $path -Force }
    Move-Item -LiteralPath $stageModule -Destination $destination
    Copy-Item -LiteralPath $stageToken -Destination $sourcePath -Force
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $sourceHash -or
        (Get-Content -LiteralPath $sourcePath -Raw).Trim() -ne $sourceToken) {
        throw "The installed Qpro module or face-tracking source failed verification."
    }
} catch {
    $failure = $_.Exception.Message
    $rollbackErrors = @()
    if ($commitStarted) {
        foreach ($path in $qproModulePaths) {
            if (Test-Path -LiteralPath $path) {
                try { Assert-QproModule $path; Remove-Item -LiteralPath $path -Force }
                catch { $rollbackErrors += $_.Exception.Message }
            }
        }
        foreach ($path in $existingQproModules) {
            try {
                if (Test-Path -LiteralPath $path) { throw "Could not restore $path because a file remains at that path." }
                Copy-Item -LiteralPath (Join-Path $recoveryDirectory (Split-Path -Leaf $path)) -Destination $path
            }
            catch { $rollbackErrors += $_.Exception.Message }
        }
        try {
            if ($configExisted) {
                Copy-Item -LiteralPath (Join-Path $recoveryDirectory "tracking-source.txt") -Destination $sourcePath -Force
            } elseif (Test-Path -LiteralPath $sourcePath) {
                Remove-Item -LiteralPath $sourcePath -Force
            }
        } catch { $rollbackErrors += $_.Exception.Message }
    }
    foreach ($moved in $movedModules) {
        try {
            if (-not (Test-Path -LiteralPath $moved.Installed)) {
                Move-Item -LiteralPath $moved.Saved -Destination $moved.Installed
            }
        } catch { $rollbackErrors += $_.Exception.Message }
    }
    $recovery = if (Test-Path -LiteralPath $recoveryDirectory) { " Recovery copies are in $recoveryDirectory." } else { "" }
    $rollback = if ($rollbackErrors.Count -gt 0) { " Rollback issues: $($rollbackErrors -join '; ')." } else { "" }
    throw "Installing the Qpro $sourceName module failed: $failure.$recovery$rollback"
} finally {
    if (Test-Path -LiteralPath $stageModule) { Remove-Item -LiteralPath $stageModule -Force }
    if (Test-Path -LiteralPath $stageToken) { Remove-Item -LiteralPath $stageToken -Force }
}

Write-Host "Installed the Qpro $sourceName VRCFaceTracking module: $destination"
if (Test-Path -LiteralPath $recoveryDirectory -PathType Container) {
    Write-Host "Recovery copies of the previous Qpro module and source are in $recoveryDirectory"
}
Write-Host "The alternate Qpro source module is uninstalled. Restart VRCFaceTracking to load $sourceName. Do not also enable a separate Virtual Desktop or Steam Link VRCFT module; Qpro supplies the selected source's face tracking and opt-in overrides."
