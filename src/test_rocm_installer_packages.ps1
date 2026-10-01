param([Parameter(Mandatory = $true)][string]$Python)

$ErrorActionPreference = 'Stop'
$installerPath = Join-Path $PSScriptRoot 'Install-QproRocm.ps1'
$tokens = $null
$parseErrors = $null
$installerAst = [System.Management.Automation.Language.Parser]::ParseFile($installerPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }

# Load only the pure helpers, never execute setup or touch a real ROCm venv.
foreach ($helper in @('Get-QproNormalizedGpuName', 'Get-QproRocm10Packages', 'Get-QproRocmPackageProbeCode', 'Invoke-QproRocmPythonProbe', 'Assert-QproRocmPythonProbe', 'Repair-QproRocm10HostWheels')) {
    $functionAst = $installerAst.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $helper }, $true)
    if ($null -eq $functionAst) { throw "Missing installer helper: $helper" }
    . ([scriptblock]::Create($functionAst.Extent.Text))
}

function Assert-Equal($Expected, $Actual, [string]$Context) {
    if ($Expected -ne $Actual) { throw "$Context expected '$Expected', got '$Actual'" }
}

$gfx1100Packages = @(Get-QproRocm10Packages 'gfx1100')
$gfx1031Packages = @(Get-QproRocm10Packages 'gfx1031')
$gfx1201Packages = @(Get-QproRocm10Packages 'gfx1201')
Assert-Equal $true ($gfx1100Packages -contains 'amd-torch-device-gfx1100==2.13.0+rocm10.0.0') 'Exact RX 7900 device package'
Assert-Equal $true ($gfx1100Packages -contains 'amd-torch-device-gfx110x==2.13.0+rocm10.0.0') 'Additional RDNA3 family package'
Assert-Equal $true ($gfx1031Packages -contains 'amd-torch-device-gfx1031==2.13.0+rocm10.0.0') 'RX 6700 XT device package'
Assert-Equal $true ($gfx1201Packages -contains 'amd-torch-device-gfx12-0==2.13.0+rocm10.0.0') 'Additional RDNA4 family package'
Assert-Equal 9 $gfx1031Packages.Count 'RX 6700 XT concrete package count'
Assert-Equal 'Radeon RX 6700 XT' (Get-QproNormalizedGpuName ([PSCustomObject]@{Name='AMD Radeon(TM) RX 6700XT'})) 'Compact RX model normalization'
Assert-Equal 'Radeon RX 6700 XT' (Get-QproNormalizedGpuName ([PSCustomObject]@{Name='AMD RX6700XT'})) 'Compact RX prefix normalization'
Assert-Equal 'Radeon RX 6700 XT Mobile' (Get-QproNormalizedGpuName ([PSCustomObject]@{Name='AMD Radeon RX 6700 XT Mobile'})) 'Unknown suffix preservation'

$probeCode = Get-QproRocmPackageProbeCode
$fixturePrelude = @'
import base64, importlib, importlib.metadata as metadata, json, sys
from email.message import Message
from types import ModuleType, SimpleNamespace
fixture_packages = dict(item.split('==', 1) for item in json.loads(base64.b64decode(sys.argv[1])))
fixture_case = sys.argv[4]
fixture_target = sys.argv[2]
if fixture_case == 'missing_device':
    fixture_packages.pop('amd-torch-device-' + fixture_target)
if fixture_case == 'wrong_version':
    fixture_packages['torch'] = '2.9.1+rocm7.2.1'
def fixture_version(name):
    if name not in fixture_packages:
        raise metadata.PackageNotFoundError(name)
    return fixture_packages[name]
def fixture_metadata(name):
    result = Message()
    if fixture_case != 'missing_extra':
        result['Provides-Extra'] = 'device-' + fixture_target
    return result
metadata.version = fixture_version
metadata.metadata = fixture_metadata
for name in ('torch', 'torchgen', 'torchvision', 'torchaudio', 'cv2', 'numpy', 'qpro_gpu'):
    sys.modules[name] = ModuleType(name)
torch = sys.modules['torch']
torch.__version__ = '2.13.0+rocm10.0.0'
torch.version = SimpleNamespace(rocm='10.0.0', hip='7.15.26333')
def no_gpu_query(*args):
    raise AssertionError('Package verification must not enumerate GPUs')
torch.cuda = SimpleNamespace(is_available=no_gpu_query, device_count=no_gpu_query)
sys.modules['qpro_gpu'].is_rocm_10_torch_build = lambda t: True
sys.modules['qpro_gpu'].is_rocm_721_torch_build = lambda t: True
real_import_module = importlib.import_module
def fixture_import_module(name):
    if name == 'torchgen' and fixture_case == 'missing_torchgen':
        raise ModuleNotFoundError('No module named \'torchgen\'')
    return real_import_module(name)
importlib.import_module = fixture_import_module
if fixture_case == 'missing_cv2':
    sys.modules['cv2'] = None
'@

function Test-PackageProbe([string]$Target, [string]$Case, [int]$ExpectedExit, [string]$ExpectedOutput = '') {
    $specs = @(Get-QproRocm10Packages $Target)
    $encodedSpecs = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $specs -Compress)))
    $arguments = @($encodedSpecs, $Target, '1', $Case)
    $result = Invoke-QproRocmPythonProbe $Python ($fixturePrelude + "`n" + $probeCode) $arguments
    Assert-Equal $ExpectedExit $result.ExitCode "Package probe $Target/$Case"
    if ($ExpectedOutput -and ($result.Output -join "`n") -notlike "*$ExpectedOutput*") {
        throw "Package probe $Target/$Case lost its useful error: $($result.Output -join ' ')"
    }
    return $result
}

Test-PackageProbe 'gfx1100' 'complete' 0 'packages and Python imports verified' | Out-Null
Test-PackageProbe 'gfx1031' 'complete' 0 'packages and Python imports verified' | Out-Null
Test-PackageProbe 'gfx1201' 'complete' 0 'packages and Python imports verified' | Out-Null
Test-PackageProbe 'gfx1100' 'missing_device' 11 'amd-torch-device-gfx1100' | Out-Null
Test-PackageProbe 'gfx1100' 'wrong_version' 11 'expected 2.13.0+rocm10.0.0' | Out-Null
Test-PackageProbe 'gfx1100' 'missing_extra' 12 'Installed torch metadata is incomplete' | Out-Null
$torchgenFailure = Test-PackageProbe 'gfx1100' 'missing_torchgen' 12 "No module named 'torchgen'"
Test-PackageProbe 'gfx1100' 'missing_cv2' 13 'Qpro runtime dependency import failed before GPU detection' | Out-Null

$legacyArguments = @([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('[]')), '-', '0', 'complete')
$legacyResult = Invoke-QproRocmPythonProbe $Python ($fixturePrelude + "`n" + $probeCode) $legacyArguments
Assert-Equal 0 $legacyResult.ExitCode 'Legacy ROCm package probe'

$errorMessage = 'Package import verification failed before GPU detection.'
try {
    Assert-QproRocmPythonProbe $torchgenFailure $errorMessage
    throw 'An import failure was incorrectly accepted.'
} catch {
    Assert-Equal $errorMessage $_.Exception.Message 'Import failure classification'
}

# Record the real repair helper's pip arguments without invoking pip. It must
# replace damaged host files once while retaining the large device/SDK packs.
$script:fixturePipCalls = @()
$script:fixturePipExit = 0
function Invoke-QproFixturePip {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$PipArguments)
    $script:fixturePipCalls += ,$PipArguments
    $global:LASTEXITCODE = $script:fixturePipExit
}
Repair-QproRocm10HostWheels 'Invoke-QproFixturePip' $gfx1100Packages
Assert-Equal 2 $script:fixturePipCalls.Count 'Targeted host repair and dependency reconciliation'
Assert-Equal $true ($script:fixturePipCalls[0] -contains '--force-reinstall') 'Repair restores already-satisfied host files'
Assert-Equal $true ($script:fixturePipCalls[0] -contains '--no-deps') 'Repair preserves installed device and SDK packages'
Assert-Equal $false (($script:fixturePipCalls[0] -join ' ') -like '*amd-torch-device*') 'Repair does not force-download GPU kernels'
Assert-Equal $false ($script:fixturePipCalls[1] -contains '--force-reinstall') 'Dependency reconciliation reuses installed packages'
Assert-Equal $true ($script:fixturePipCalls[1] -contains 'amd-torch-device-gfx110x==2.13.0+rocm10.0.0') 'Dependency reconciliation includes family kernel pack'

$script:fixturePipCalls = @()
$script:fixturePipExit = 1
try {
    Repair-QproRocm10HostWheels 'Invoke-QproFixturePip' $gfx1100Packages
    throw 'A failed host reinstall was incorrectly accepted.'
} catch {
    Assert-Equal 'Repairing AMD PyTorch host wheels failed. See the package download error above.' $_.Exception.Message 'Host repair failure classification'
}
Assert-Equal 1 $script:fixturePipCalls.Count 'Failed host reinstall stops before dependency reconciliation'
Write-Host 'PASS: complete target packages, damaged metadata/torchgen, targeted repair, dependency failures, legacy imports and compact GPU names'
