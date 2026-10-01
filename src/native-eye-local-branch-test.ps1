param(
    [switch]$RestoreOnly,
    [switch]$RestoreIfActive,
    [switch]$Personalized,
    [switch]$Calibrate,
    [switch]$RuntimePreview,
    [switch]$VrcftOutput,
    [switch]$NoWindow,
    [string]$AdbTarget = "",
    [switch]$Wireless,
    [string]$CalibrationOutput = ".\calibration\qpro-independent-visual-axis-v2.json",
    [string]$OverlayPath = ".\third_party\BabbleCalibration-Windows-1.0.8\BabbleCalibration.x86_64.exe",
    [ValidateRange(20, 300)]
    [int]$GazeSeconds = 60,
    [ValidateRange(20, 300)]
    [int]$ConvergenceSeconds = 80,
    [ValidateRange(0, 30)]
    [int]$HeadlessSeconds = 0,
    [string]$StopFile = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$adb = if (-not [string]::IsNullOrWhiteSpace($env:QPRO_ADB) -and (Test-Path -LiteralPath $env:QPRO_ADB)) {
    [System.IO.Path]::GetFullPath($env:QPRO_ADB)
} elseif (Test-Path -LiteralPath (Join-Path $root "platform-tools\adb.exe")) {
    Join-Path $root "platform-tools\adb.exe"
} else {
    Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"
}
$hadAndroidSerial = Test-Path Env:ANDROID_SERIAL
$previousAndroidSerial = $env:ANDROID_SERIAL

function Resolve-WorkspacePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return "" }
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $root $Path))
}

if ($Wireless -and [string]::IsNullOrWhiteSpace($AdbTarget)) {
    $wirelessConfig = Join-Path $root "config\wireless-headset.json"
    if (-not (Test-Path -LiteralPath $wirelessConfig)) {
        throw "No saved wireless headset exists. Run enable-quest-wireless.ps1 with USB connected first."
    }
    $AdbTarget = (Get-Content -LiteralPath $wirelessConfig -Raw | ConvertFrom-Json).adbTarget
}
$python = if (-not [string]::IsNullOrWhiteSpace($env:QPRO_PYTHON)) { $env:QPRO_PYTHON } else { Join-Path $root ".venv\Scripts\python.exe" }
$pythonFallback = Join-Path $root ".venv\Scripts\qpro-python-console.exe"
if (-not (Test-Path -LiteralPath $python) -and (Test-Path -LiteralPath $pythonFallback)) {
    $python = $pythonFallback
}
$localModel = Join-Path $root "research\seacliff_eye_model\bolt-independent-axes.ptl"
$modelManifest = Join-Path $root "research\seacliff_eye_model\bolt-independent-axes.manifest.json"
$modelPreflight = Join-Path $root "prepare_eye_model.py"
$gazeSessionId = [Guid]::NewGuid().ToString('N')
$remoteModel = "/data/local/tmp/qpro-seacliff-independent-axes-$gazeSessionId.ptl"
$targetModel = "/odm/etc/eyetracking/runtime/models/Seacliff_V1_5/fbnet/int8/experimental/bolt/bolt.ptl"
$modelProperty = "persist.device_config.oculus_shared_vision.oculus_eyetracking_enable_experimental_model"
$gazeJournalPath = "/data/local/tmp/qpro-independent-gaze-session.json"
$overlay = Resolve-WorkspacePath $OverlayPath
$calibrationOutputPath = Resolve-WorkspacePath $CalibrationOutput

if (-not (Test-Path -LiteralPath $adb)) { throw "ADB not found. Re-extract the release so platform-tools\adb.exe is present." }
if (-not ($RestoreOnly -or $RestoreIfActive) -and -not (Test-Path -LiteralPath $python)) { throw "Project Python environment not found under .venv\Scripts." }
if ($Calibrate -and -not ($RestoreOnly -or $RestoreIfActive)) {
    if (-not (Test-Path -LiteralPath $overlay)) { throw "BabbleCalibration not found: $overlay" }
    if (-not (Get-Process -Name "vrserver" -ErrorAction SilentlyContinue)) {
        throw "Start SteamVR before visual-axis calibration."
    }
}
if (($RuntimePreview -or $VrcftOutput) -and -not ($RestoreOnly -or $RestoreIfActive) -and -not (Test-Path -LiteralPath $calibrationOutputPath)) {
    throw "Independent visual-axis calibration not found: $calibrationOutputPath"
}
if ($VrcftOutput -and -not ($RestoreOnly -or $RestoreIfActive) -and -not (Get-Process -Name "VRCFaceTracking" -ErrorAction SilentlyContinue)) {
    throw "Start VRCFaceTracking before enabling gaze-only output."
}

if (-not [string]::IsNullOrWhiteSpace($AdbTarget)) {
    $env:ANDROID_SERIAL = $AdbTarget.Trim()
}

function Invoke-Root([string]$Command, [switch]$AllowFailure) {
    # Windows PowerShell can promote a native process's stderr to a terminating
    # NativeCommandError when the script-wide preference is Stop. Capture the
    # process result first, then apply our own exit-code policy.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = & $adb shell su -c $Command 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "Headset command failed: $Command`n$output"
    }
    return ($output | Out-String).Trim()
}

function Wait-TrackingService {
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ((Invoke-Root "getprop init.svc.trackingservice" -AllowFailure) -eq "running") {
            Start-Sleep -Milliseconds 1500
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw "The headset tracking service did not return to running state."
}

function Read-PreparedModelPath {
    if (-not (Test-Path -LiteralPath $modelManifest)) { return }
    $manifest = Get-Content -LiteralPath $modelManifest -Raw | ConvertFrom-Json
    $path = [string]$manifest.modelPath
    if ($path -notmatch '^/odm/etc/eyetracking/runtime/models/[A-Za-z0-9_./-]+/bolt\.ptl$' -or $path.Contains('..')) {
        throw "The prepared gaze manifest has an invalid headset model path. Prepare independent gaze again."
    }
    return $path
}

function Get-TargetMount {
    $mountTable = Invoke-Root "cat /proc/mounts"
    foreach ($line in ($mountTable -split "`r?`n")) {
        $fields = $line -split '\s+'
        if ($fields.Length -ge 2 -and $fields[1] -eq $targetModel) { return $line }
    }
    return ""
}

function Get-GazeDeviceSerial {
    $serial = Invoke-Root "getprop ro.boot.serialno"
    if ([string]::IsNullOrWhiteSpace($serial) -or $serial -eq "unknown") {
        $serial = Invoke-Root "getprop ro.serialno"
    }
    if ($serial -notmatch '^[A-Za-z0-9_.:-]{1,128}$' -or $serial -eq "unknown") {
        throw "The headset identity could not be verified for eye-model recovery. No tracking was changed."
    }
    return $serial
}

function Get-GazeFileHash([string]$Path) {
    $hash = ((Invoke-Root "sha256sum '$Path'") -split '\s+')[0].ToLowerInvariant()
    if ($hash -notmatch '^[0-9a-f]{64}$') { throw "Could not verify the eye-model file hash. No tracking was changed." }
    return $hash
}

function Get-GazeOwnerHost {
    $bytes = [Text.Encoding]::UTF8.GetBytes([Environment]::MachineName + ':' + [Environment]::UserName)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hasher.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $hasher.Dispose() }
}

function Assert-GazeOwnerStopped($Journal) {
    if ($Journal.ownerHost -ne (Get-GazeOwnerHost)) {
        throw "The recorded Qpro session belongs to another PC or user. Recovery ownership could not be confirmed; no tracking was changed."
    }
    try { $owner = [Diagnostics.Process]::GetProcessById([int]$Journal.ownerPid) }
    catch [ArgumentException] { return }
    try {
        if ($owner.HasExited -or $owner.StartTime.ToUniversalTime().Ticks.ToString() -ne $Journal.ownerStarted) { return }
        # The creating PowerShell's finally may restore its own session. A
        # second Hub must not recover a session whose owner is still running.
        if ($owner.Id -ne $PID) { throw "The recorded Qpro gaze process is still running. Stop its owning Hub before recovery; no tracking was changed." }
    } finally { $owner.Dispose() }
}

function Read-GazeJournal {
    $text = Invoke-Root "if test -e '$gazeJournalPath'; then cat '$gazeJournalPath'; else echo __QPRO_NO_GAZE_JOURNAL__; fi"
    if ($text -eq "__QPRO_NO_GAZE_JOURNAL__") { return $null }
    if ((Invoke-Root "stat -c '%u:%a' '$gazeJournalPath'") -ne "0:600") {
        throw "The eye-model recovery journal is not a private root-owned Qpro file. No tracking was changed."
    }
    if ($text.Length -gt 4096) { throw "The eye-model recovery journal is invalid. No tracking was changed." }
    try { $journal = $text | ConvertFrom-Json }
    catch { throw "The eye-model recovery journal is not valid JSON. No tracking was changed." }
    foreach ($name in @('format', 'owner', 'sessionId', 'ownerHost', 'ownerPid', 'ownerStarted', 'deviceSerial', 'bootId', 'targetModel', 'remoteModel', 'modelProperty', 'sourceHash', 'sourceIdentity', 'originalModelHash', 'originalProperty')) {
        if ($null -eq $journal.PSObject.Properties[$name] -or $journal.$name -isnot [string]) {
            throw "The eye-model recovery journal has missing or invalid fields. No tracking was changed."
        }
    }
    if ($journal.format -ne 'qpro-headset-gaze-session-v1' -or $journal.owner -ne 'QproFaceTracking' -or
        $journal.sessionId -notmatch '^[0-9a-f]{32}$' -or $journal.deviceSerial -notmatch '^[A-Za-z0-9_.:-]{1,128}$' -or
        $journal.ownerHost -notmatch '^[0-9a-f]{64}$' -or $journal.ownerPid -notmatch '^[1-9][0-9]{0,9}$' -or [long]$journal.ownerPid -gt [int]::MaxValue -or
        $journal.ownerStarted -notmatch '^[0-9]{1,19}$' -or
        $journal.bootId -notmatch '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$' -or
        $journal.targetModel -notmatch '^/odm/etc/eyetracking/runtime/models/[A-Za-z0-9_./-]+/bolt\.ptl$' -or $journal.targetModel.Contains('..') -or
        $journal.remoteModel -ne "/data/local/tmp/qpro-seacliff-independent-axes-$($journal.sessionId).ptl" -or $journal.modelProperty -ne $modelProperty -or
        $journal.sourceHash -notmatch '^[0-9a-f]{64}$' -or $journal.originalModelHash -notmatch '^[0-9a-f]{64}$' -or
        $journal.sourceIdentity -notmatch '^\d+:\d+$' -or $journal.originalProperty -notin @('', 'false', 'true', '0', '1')) {
        throw "The eye-model recovery journal is not a valid Qpro session. No tracking was changed."
    }
    if ($journal.deviceSerial -ne (Get-GazeDeviceSerial)) {
        throw "The eye-model recovery journal belongs to another headset. No tracking was changed."
    }
    return $journal
}

function New-GazeJournal([string]$OriginalProperty, [string]$PatchedHash, [string]$ExpectedOriginalHash) {
    if ($OriginalProperty -notin @('', 'false', 'true', '0', '1') -or $PatchedHash -notmatch '^[0-9a-f]{64}$' -or $ExpectedOriginalHash -notmatch '^[0-9a-f]{64}$') {
        throw "The original eye-model state could not be recorded safely. No tracking was changed."
    }
    if ($null -ne (Read-GazeJournal) -or -not [string]::IsNullOrWhiteSpace((Get-TargetMount))) {
        throw "An eye-model session is already active. Recover it before starting another gaze session."
    }
    $identity = Invoke-Root "stat -c '%d:%i' '$remoteModel'"
    if ($identity -notmatch '^\d+:\d+$' -or (Get-GazeFileHash $remoteModel) -ne $PatchedHash) {
        throw "The temporary eye-model source could not be verified. No tracking was changed."
    }
    $bootId = Invoke-Root "cat /proc/sys/kernel/random/boot_id"
    if ($bootId -notmatch '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$') {
        throw "The headset boot identity could not be verified. No tracking was changed."
    }
    if ((Invoke-Root "getprop $modelProperty") -ne $OriginalProperty) {
        throw "The headset eye-model selection changed during startup. No tracking was changed."
    }
    $originalHash = Get-GazeFileHash $targetModel
    if ($originalHash -ne $ExpectedOriginalHash) {
        throw "The headset eye model changed after compatibility checking. No tracking was changed."
    }
    $journal = [PSCustomObject]@{
        format = 'qpro-headset-gaze-session-v1'; owner = 'QproFaceTracking'
        ownerHost = Get-GazeOwnerHost; ownerPid = $PID.ToString(); ownerStarted = (Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks.ToString()
        sessionId = $gazeSessionId; deviceSerial = Get-GazeDeviceSerial
        bootId = $bootId; targetModel = $targetModel; remoteModel = $remoteModel; modelProperty = $modelProperty
        sourceHash = $PatchedHash; sourceIdentity = $identity; originalModelHash = $originalHash
        originalProperty = $OriginalProperty
    }
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($journal | ConvertTo-Json -Compress)))
    $temporary = $gazeJournalPath + '.' + $journal.sessionId + '.tmp'
    # Commit the restore state before any tracking change. A hard process stop
    # or reboot can remove the mount but leave the persist.* property enabled.
    Invoke-Root "umask 077; if test -e '$gazeJournalPath'; then exit 1; fi; printf '%s' '$encoded' | base64 -d > '$temporary' && chown root:root '$temporary' && chmod 0600 '$temporary' && ln '$temporary' '$gazeJournalPath' && rm '$temporary' && sync" | Out-Null
    $script:cleanupNeeded = $true
    $saved = Read-GazeJournal
    if ($null -eq $saved -or $saved.sessionId -ne $journal.sessionId) {
        throw "The eye-model recovery journal could not be committed. No tracking was changed."
    }
}

function Restore-StockModel($Journal) {
    Assert-GazeOwnerStopped $Journal
    $script:targetModel = $Journal.targetModel
    $script:remoteModel = $Journal.remoteModel
    $sourceIdentity = Invoke-Root "stat -c '%d:%i' '$remoteModel'"
    if ($sourceIdentity -ne $Journal.sourceIdentity -or (Get-GazeFileHash $remoteModel) -ne $Journal.sourceHash) {
        throw "The recorded temporary eye-model source has changed or is missing. Recovery was not applied."
    }
    $mounted = -not [string]::IsNullOrWhiteSpace((Get-TargetMount))
    if ($mounted) {
        if ((Invoke-Root "stat -c '%d:%i' '$targetModel'") -ne $Journal.sourceIdentity -or
            (Get-GazeFileHash $targetModel) -ne $Journal.sourceHash) {
            throw "A foreign eye-model mount is active. Remove it through its own module; Qpro did not change it."
        }
    } elseif ((Get-GazeFileHash $targetModel) -ne $Journal.originalModelHash) {
        throw "The headset eye model changed since the recorded Qpro session. Recovery was not applied."
    }
    $property = Invoke-Root "getprop $modelProperty"
    if ($property -ne $Journal.originalProperty -and $property -ne 'true') {
        throw "The headset eye-model selection changed outside the recorded Qpro session. Recovery was not applied."
    }
    if ($mounted -or $property -ne $Journal.originalProperty) {
        Write-Host "HEADSET_TRACKING_RESTART phase=restore status=begin"
        try {
            Invoke-Root "stop trackingservice" | Out-Null
            Invoke-Root "setprop $modelProperty '$($Journal.originalProperty)'" | Out-Null
            if ($mounted) { Invoke-Root "umount '$targetModel'" | Out-Null }
        } finally {
            # A failed property/unmount operation retains the journal for retry.
            Invoke-Root "start trackingservice" -AllowFailure | Out-Null
        }
        Wait-TrackingService
        Write-Host "HEADSET_TRACKING_RESTART phase=restore status=running"
    }
    if (-not [string]::IsNullOrWhiteSpace((Get-TargetMount)) -or
        (Invoke-Root "getprop $modelProperty") -ne $Journal.originalProperty -or
        (Get-GazeFileHash $targetModel) -ne $Journal.originalModelHash) {
        throw "The recorded pre-Qpro eye-model state could not be confirmed. Keep the Hub open and check Activity."
    }
    Invoke-Root "rm '$gazeJournalPath'" | Out-Null
    Invoke-Root "rm -f '$remoteModel'" -AllowFailure | Out-Null
}

function Invoke-GazeRecovery {
    try {
        $journal = Read-GazeJournal
        if ($null -ne $journal) {
            Restore-StockModel $journal
            Write-Host "Recorded pre-Qpro eye-model state restored. Other modules were not disabled."
            Write-Host "QPRO_GAZE_RECOVERY restored"
            return
        }
        if (-not [string]::IsNullOrWhiteSpace((Get-TargetMount))) {
            throw "An eye-model mount exists without a verified recovery journal. Qpro did not remove it or change the headset selection."
        }
        $property = Invoke-Root "getprop $modelProperty"
        if ($property -notin @('', 'false', '0')) {
            throw "The experimental eye-model selection is enabled without a Qpro recovery journal. It may belong to Magisk or an interrupted older session. Qpro did not change it; stock gaze cannot be confirmed."
        }
        Write-Host "No recorded Qpro gaze session remains. Other headset modules were not checked or disabled."
        Write-Host "QPRO_GAZE_RECOVERY none"
    } catch {
        Write-Host "QPRO_GAZE_RECOVERY unconfirmed"
        throw
    }
}

try {
    & $adb get-state | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No authorized Quest was found over ADB." }
    $rootProbe = & $adb shell su -c id 2>&1
    if ($LASTEXITCODE -ne 0 -or ($rootProbe -join "`n") -notmatch 'uid=0\(root\)') {
        throw "Magisk root is not granted to Android Shell. On the headset open Magisk > Superuser and enable Shell (or ADB Shell), then retry."
    }
    if (-not [string]::IsNullOrWhiteSpace($AdbTarget)) {
        Write-Host "Independent-eye ADB target: $AdbTarget"
    }

    $preparedPath = ""
    try { $preparedPath = Read-PreparedModelPath }
    catch {
        if (-not ($RestoreOnly -or $RestoreIfActive)) { throw }
        Write-Host "Prepared gaze manifest is invalid; checking the known Qpro mount path for cleanup."
    }
    if (-not [string]::IsNullOrWhiteSpace($preparedPath)) { $targetModel = $preparedPath }

    if ($RestoreOnly) {
        Invoke-GazeRecovery
        exit 0
    }
    if ($RestoreIfActive) {
        Invoke-GazeRecovery
        exit 0
    }
    if ($null -ne (Read-GazeJournal)) {
        throw "A recorded Qpro gaze session remains. Stop its owning Hub first, or use gaze recovery after that process has ended. No new tracking was started."
    }
    $existingMount = Get-TargetMount
    if (-not [string]::IsNullOrWhiteSpace($existingMount)) {
        throw "An eye-model mount is already active. Close its viewer with Q and wait for restoration. If the Qpro process is gone, use -RestoreOnly to remove a verified Qpro mount; other modules must remove their own mount."
    }
    if (-not (Test-Path -LiteralPath $localModel)) {
        throw "Patched research model not found: $localModel"
    }
    if (-not (Test-Path -LiteralPath $modelManifest)) {
        throw "The prepared gaze manifest is missing. Use First-time setup to prepare independent gaze again."
    }
    if (-not (Test-Path -LiteralPath $modelPreflight)) {
        throw "The gaze compatibility check is missing. Re-extract the complete Qpro release."
    }
    Write-Host "Checking prepared eye model and headset firmware before changing tracking..."
    & $python $modelPreflight --check-prepared --adb $adb
    if ($LASTEXITCODE -ne 0) {
        throw "Independent gaze cannot start safely on this headset. See the compatibility reason above, then prepare independent gaze again if the headset was updated. No headset tracking was changed."
    }
    Write-Host "Prepared eye model matches this headset and supported tracking engine."

    $originalProperty = Invoke-Root "getprop $modelProperty"
    if ($originalProperty -notin @('', 'false', '0')) {
        throw "The experimental eye-model selection is already enabled without a recorded Qpro session. It may belong to Magisk or an interrupted older Qpro version. No new tracking was applied; identify that method before starting Qpro gaze."
    }
    $cleanupNeeded = $false
    try {
    & $adb push $localModel $remoteModel | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Could not copy the research model to the headset." }
    Invoke-Root "chown root:root '$remoteModel'" | Out-Null
    Invoke-Root "chmod 0644 '$remoteModel'" | Out-Null
    Invoke-Root "chcon u:object_r:vendor_configs_file:s0 '$remoteModel'" | Out-Null
    $localHash = (Get-FileHash -LiteralPath $localModel -Algorithm SHA256).Hash.ToLowerInvariant()
    $preparedManifest = Get-Content -LiteralPath $modelManifest -Raw | ConvertFrom-Json
    if ($localHash -ne $preparedManifest.patchedSha256) { throw "The prepared eye-model patch changed during startup. No tracking was changed." }
    New-GazeJournal $originalProperty $localHash ([string]$preparedManifest.sourceSha256)
    Write-Host "QPRO_GAZE_SESSION applied"
    Invoke-Root "mount --bind '$remoteModel' '$targetModel'" | Out-Null

    $remoteHash = ((Invoke-Root "sha256sum '$targetModel'") -split "\s+")[0].ToLowerInvariant()
    if ($localHash -ne $remoteHash) { throw "The temporary model failed its headset hash check." }

    Invoke-Root "setprop $modelProperty true" | Out-Null
    Write-Host "HEADSET_TRACKING_RESTART phase=apply status=begin"
    Invoke-Root "stop trackingservice" | Out-Null
    Invoke-Root "start trackingservice" | Out-Null
    Wait-TrackingService
    Write-Host "HEADSET_TRACKING_RESTART phase=apply status=running"
    Write-Host "Temporary independent Meta gaze branch active. Q restores the recorded pre-Qpro eye state."

    if ($RuntimePreview -or $VrcftOutput) {
        $runtimeArguments = @(
            (Join-Path $root "independent_visual_axis_runtime.py"),
            "--adb", $adb,
            "--calibration", $calibrationOutputPath
        )
        if ($VrcftOutput) { $runtimeArguments += @("--output-vrcft", "--sample-timeout-seconds", "20") }
        if ($NoWindow) { $runtimeArguments += "--no-window" }
        if ($HeadlessSeconds -gt 0) {
            $runtimeArguments += @("--headless-seconds", $HeadlessSeconds)
        }
        if (-not [string]::IsNullOrWhiteSpace($StopFile)) {
            $runtimeArguments += @(
                "--stop-file",
                (Resolve-WorkspacePath $StopFile)
            )
        }
        & $python @runtimeArguments
    }
    elseif ($Calibrate) {
        & $python (Join-Path $root "native_raw_eye_probe.py") `
            --adb $adb `
            --title "Quest Pro independent visual-axis calibration" `
            --notice "TEMPORARY MODEL OVERRIDE - Meta personalization after local branch; Q restores stock" `
            --calibration-overlay $overlay `
            --calibration-output $calibrationOutputPath `
            --gaze-seconds $GazeSeconds `
            --convergence-seconds $ConvergenceSeconds
    }
    elseif ($Personalized) {
        & $python (Join-Path $root "native_raw_eye_probe.py") `
            --adb $adb `
            --title "Quest Pro independent personalized visual axes" `
            --notice "TEMPORARY MODEL OVERRIDE - Meta per-eye calibration after local branch; Q restores stock" `
            --instruction "Test a fixed target around the center and corners, then drift or close one eye; the other ray must remain fixed."
    }
    else {
        & $python (Join-Path $root "native_eye_stage_probe.py") `
            --adb $adb `
            --title "Quest Pro Meta local-branch gaze test" `
            --notice "TEMPORARY MODEL OVERRIDE - stock model is restored when Q quits" `
            --instruction "Close one eye or drift only the right eye; the two bottom axes should now remain independent."
    }
    if ($LASTEXITCODE -ne 0) { throw "The local-branch gaze viewer failed." }
    }
    finally {
        if ($cleanupNeeded) {
            Invoke-GazeRecovery
        }
    }
}
finally {
    if ($hadAndroidSerial) {
        $env:ANDROID_SERIAL = $previousAndroidSerial
    }
    else {
        Remove-Item Env:ANDROID_SERIAL -ErrorAction SilentlyContinue
    }
}
