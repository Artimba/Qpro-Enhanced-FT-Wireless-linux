param(
    [string]$PairingEndpoint = "",
    [string]$AdbTarget = "",
    [string]$PairingCode = ""
)

$ErrorActionPreference = "Stop"
$adb = Join-Path $PSScriptRoot "platform-tools\adb.exe"
if (-not (Test-Path -LiteralPath $adb)) {
    throw "Bundled platform-tools\adb.exe is missing. Extract the full ZIP again."
}
if (-not [string]::IsNullOrWhiteSpace($AdbTarget)) {
    $ErrorActionPreference = "Continue"
    $connectedState = (& $adb -s $AdbTarget get-state 2>&1) -join "`n"
    $connectedExit = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($connectedExit -eq 0 -and $connectedState.Trim() -eq "device") {
        Write-Host "The Quest is already connected at $AdbTarget. Pairing is not needed; checking Magisk root."
        & (Join-Path $PSScriptRoot "Connect-QproWireless.ps1") -AdbTarget $AdbTarget
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        exit 0
    }
}
if ([string]::IsNullOrWhiteSpace($PairingEndpoint)) {
    $PairingEndpoint = Read-Host "Pairing IP:port shown on the headset"
}
if ($PairingEndpoint -notmatch '^(?<ip>\d{1,3}(?:\.\d{1,3}){3}):(?<port>\d{2,5})$') {
    throw "Enter the exact pairing IP:port shown on the headset."
}
if (-not [string]::IsNullOrWhiteSpace($AdbTarget) -and
    $PairingEndpoint.Equals($AdbTarget, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The pairing port must be the temporary one shown with the six-digit code. Use the regular Quest IP:port with Connect to Quest."
}
$pairIp = $Matches['ip']
$pairPort = [int]$Matches['port']
$parsedIp = $null
if (-not [System.Net.IPAddress]::TryParse($pairIp, [ref]$parsedIp) -or
    $parsedIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or
    $pairPort -lt 1024 -or $pairPort -gt 65535) {
    throw "The pairing IP or port is invalid."
}
if ([string]::IsNullOrWhiteSpace($PairingCode)) {
    $PairingCode = Read-Host "Six-digit pairing code shown on the headset"
}
if ($PairingCode -notmatch '^\d{6}$') { throw "The pairing code must be six digits." }

$ErrorActionPreference = "Continue"
$pairOutput = (& $adb pair $PairingEndpoint $PairingCode 2>&1) -join "`n"
$pairExit = $LASTEXITCODE
$ErrorActionPreference = "Stop"
if ($pairExit -ne 0 -or $pairOutput -notmatch 'Successfully paired') {
    if ($pairOutput -match 'protocol fault|couldn.t read status message') {
        throw "The address did not respond as a pairing service. Open Pair using pairing code on the headset and enter its temporary IP:port and fresh six-digit code. Do not use the regular connection port. $pairOutput"
    }
    throw "Pairing failed. Keep the pairing dialog open on the headset and try again. $pairOutput"
}
Write-Host $pairOutput
Write-Host "Use the regular Wireless debugging IP:port for the connection, not the temporary pairing port."
& (Join-Path $PSScriptRoot "Connect-QproWireless.ps1") -AdbTarget $AdbTarget
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
