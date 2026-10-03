[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Zig,
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\controller-native-build')
)
$ErrorActionPreference = 'Stop'
$compiler = (Resolve-Path -LiteralPath $Zig).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $output '.zig-cache'
$reader = Join-Path $output 'qpro-controller-input'
$logicTest = Join-Path $output 'test-controller-logic.exe'
$readerTest = Join-Path $output 'test-controller-reader.exe'
$addon = Join-Path $output 'qpro_controller'
$binaryDirectory = Join-Path $addon 'bin\win64'
[IO.Directory]::CreateDirectory($binaryDirectory) | Out-Null
function Invoke-Compiler([string[]]$CompilerArguments) {
    $portableFlags = @('-s', "-ffile-prefix-map=$PSScriptRoot=controller-input", "-ffile-prefix-map=$(Split-Path $compiler -Parent)=zig")
    & $compiler @CompilerArguments @portableFlags
    if ($LASTEXITCODE -ne 0) { throw "Native compiler failed with code $LASTEXITCODE." }
}
Push-Location -LiteralPath $PSScriptRoot
try {
    Invoke-Compiler @('c++', '-target', 'x86_64-windows-gnu', '-std=c++17', '-O2', '-Wall', '-Wextra', '-Werror', 'tests/test_logic.cpp', '-o', $logicTest)
    Invoke-Compiler @('cc', '-target', 'x86_64-windows-gnu', '-std=c11', '-O2', '-Wall', '-Wextra', '-Werror', 'tests/test_reader.c', '-o', $readerTest)
    & $logicTest
    if ($LASTEXITCODE -ne 0) { throw "Controller logic checks failed with code $LASTEXITCODE." }
    & $readerTest
    if ($LASTEXITCODE -ne 0) { throw "Fake-page/lifetime checks failed with code $LASTEXITCODE." }
    Invoke-Compiler @('cc', '-target', 'aarch64-linux-musl', '-static', '-std=c11', '-O2', '-Wall', '-Wextra', '-Werror', 'headset_reader.c', '-o', $reader, '-lm')
    $driver = Join-Path $binaryDirectory 'driver_qpro_controller.dll'
    Invoke-Compiler @('c++', '-target', 'x86_64-windows-gnu', '-std=c++17', '-O2', '-shared', '-Wall', '-Wextra', '-Werror', 'steamvr_provider.cpp', '-o', $driver, '-lws2_32', '-luser32')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'addon\driver.vrdrivermanifest') -Destination $addon -Force
    [IO.Directory]::CreateDirectory((Join-Path $addon 'resources\input')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'addon\resources\settings.json') -Destination (Join-Path $addon 'resources\settings.json') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'addon\resources\input\quest_pro_touchpad.json') -Destination (Join-Path $addon 'resources\input\quest_pro_touchpad.json') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'third_party\openvr\LICENSE') -Destination (Join-Path $addon 'LICENSE.OpenVR.txt') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $addon 'README.md') -Force
    $record = [ordered]@{
        liveValidated = $false
        firmwareProfile = 'legacy-51503870024400340'
        sdkRevision = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'third_party\openvr\SDK_REVISION.txt') -Raw).Trim()
        readerSha256 = (Get-FileHash -LiteralPath $reader -Algorithm SHA256).Hash.ToLowerInvariant()
        driverSha256 = (Get-FileHash -LiteralPath $driver -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'build-info.json') -Encoding UTF8
    Write-Output "Native prototype built; staged add-on: $addon"
    Write-Output 'Only offline test programs were executed. Headset and SteamVR validation remain pending.'
}
finally { Pop-Location }
