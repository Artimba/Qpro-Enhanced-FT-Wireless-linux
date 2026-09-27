$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$customLibs = Join-Path $env:APPDATA "VRCFaceTracking\CustomLibs"
$research = Join-Path $root "research"
$destination = Join-Path $customLibs "000-Qpro.IndependentGaze.dll"
$legacyId = "7f9be083-a4f1-4e30-b28a-8e6ec878d583"
$officialId = "91a90618-b020-4064-8832-809b2ca2b3bc"

if (Get-Process -Name "VRCFaceTracking" -ErrorAction SilentlyContinue) {
    throw "Close VRCFaceTracking before uninstalling the Qpro bridge."
}

if (Test-Path -LiteralPath $destination) {
    try {
        $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($destination).Name
    } catch {
        throw "The file at $destination is not a readable Qpro bridge DLL. It was left untouched."
    }
    if ($assemblyName -ne "Qpro.GazeBridge") {
        throw "The file at $destination is not the Qpro bridge (assembly: $assemblyName). It was left untouched."
    }
    Remove-Item -LiteralPath $destination -Force
    Write-Host "Removed the Qpro VRCFaceTracking bridge: $destination"
} else {
    Write-Host "No installed Qpro bridge DLL was found. Checking for saved modules to restore."
}

function Restore-SavedModule([string]$BackupPath, [string]$ModuleId) {
    if (-not (Test-Path -LiteralPath $BackupPath -PathType Container)) { return }
    $item = Get-Item -LiteralPath $BackupPath
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The saved module is a link and was left untouched: $BackupPath"
    }
    $source = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $BackupPath).ProviderPath)
    $allowedSource = [System.IO.Path]::GetFullPath($research).TrimEnd('\') + '\'
    if (-not $source.StartsWith($allowedSource, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The saved module is outside this Qpro release: $source"
    }
    $target = [System.IO.Path]::GetFullPath((Join-Path $customLibs $ModuleId))
    $allowedTarget = [System.IO.Path]::GetFullPath($customLibs).TrimEnd('\') + '\'
    if (-not $target.StartsWith($allowedTarget, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The restore destination is outside VRCFaceTracking CustomLibs: $target"
    }
    if (Test-Path -LiteralPath $target) {
        Write-Host "Kept saved module at $source because $target already exists. No existing module was overwritten."
        return
    }
    New-Item -ItemType Directory -Path $customLibs -Force | Out-Null
    Move-Item -LiteralPath $source -Destination $target
    Write-Host "Restored saved VRCFaceTracking module: $target"
}

if (Test-Path -LiteralPath $research -PathType Container) {
    $officialBackups = @(Get-ChildItem -LiteralPath $research -Directory |
        Where-Object { $_.Name -match '^vrcft-official-virtual-desktop-backup(?:-\d{8}-\d{6})?$' } |
        Sort-Object LastWriteTimeUtc -Descending)
    if ($officialBackups.Count -gt 0) {
        Restore-SavedModule $officialBackups[0].FullName $officialId
        if ($officialBackups.Count -gt 1) {
            Write-Host "Older Virtual Desktop module backups remain in $research for manual review."
        }
    }
    Restore-SavedModule (Join-Path $research "vrcft-legacy-registry-module-backup") $legacyId
}

Write-Host "Qpro bridge uninstall finished. Restart VRCFaceTracking. Personal tongue models and captures were not changed."
Write-Host "If normal Virtual Desktop face tracking is missing and no saved module was restored, install its official VRCFaceTracking module again."
