$ErrorActionPreference = 'Stop'

# Run the actual recovery helpers against a stateful fake root/ADB boundary.
# No headset command, installed module, or live ADB server is used.
$tokens = $null
$errors = $null
$sourcePath = Join-Path $PSScriptRoot 'native-eye-local-branch-test.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($sourcePath, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($name in @('Get-TargetMount', 'Get-GazeDeviceSerial', 'Get-GazeFileHash', 'Get-GazeOwnerHost', 'Assert-GazeOwnerStopped', 'Read-GazeJournal', 'New-GazeJournal', 'Restore-StockModel', 'Invoke-GazeRecovery')) {
    $function = $ast.Find({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $function) { throw "Missing gaze recovery helper: $name" }
    . ([scriptblock]::Create($function.Extent.Text))
}

function Assert-Equal($Expected, $Actual, [string]$Context) {
    if ($Expected -ne $Actual) { throw "$Context expected '$Expected', got '$Actual'" }
}

function Reset-Fixture([string]$Original = 'false') {
    $script:gazeSessionId = '1234567890abcdef1234567890abcdef'
    $script:gazeJournalPath = '/data/local/tmp/qpro-independent-gaze-session.json'
    $script:remoteModel = "/data/local/tmp/qpro-seacliff-independent-axes-$gazeSessionId.ptl"
    $script:targetModel = '/odm/etc/eyetracking/runtime/models/Seacliff_V1_5/fbnet/int8/experimental/bolt/bolt.ptl'
    $script:modelProperty = 'persist.device_config.oculus_shared_vision.oculus_eyetracking_enable_experimental_model'
    $script:cleanupNeeded = $false
    $script:fixture = @{
        Journal = $null; JournalMode = '0:600'; Serial = 'FIXTUREQUEST'
        Property = $Original; Mount = $false; ForeignMount = $false; ModelChanged = $false
        SourceExists = $true; SourceIdentity = '100:200'; SourceHash = ('a' * 64); OriginalHash = ('b' * 64)
        BootId = '11111111-2222-3333-4444-555555555555'; Running = $true
        Failure = ''; IgnorePropertyWrite = $false; Commands = [Collections.Generic.List[string]]::new()
        Mutations = [Collections.Generic.List[string]]::new()
    }
    $script:journal = [PSCustomObject]@{
        format = 'qpro-headset-gaze-session-v1'; owner = 'QproFaceTracking'; sessionId = $gazeSessionId
        ownerHost = Get-GazeOwnerHost; ownerPid = '2147483647'; ownerStarted = '1'
        deviceSerial = $fixture.Serial; bootId = $fixture.BootId
        targetModel = $targetModel; remoteModel = $remoteModel; modelProperty = $modelProperty
        sourceHash = $fixture.SourceHash; sourceIdentity = $fixture.SourceIdentity
        originalModelHash = $fixture.OriginalHash; originalProperty = $Original
    }
}

function Save-FixtureJournal {
    $fixture.Journal = $journal | ConvertTo-Json -Compress
}

function Invoke-Root([string]$Command, [switch]$AllowFailure) {
    $fixture.Commands.Add($Command)
    if ($Command -eq "if test -e '$gazeJournalPath'; then cat '$gazeJournalPath'; else echo __QPRO_NO_GAZE_JOURNAL__; fi") {
        if ($null -eq $fixture.Journal) { return '__QPRO_NO_GAZE_JOURNAL__' }
        return $fixture.Journal
    }
    if ($Command -eq "stat -c '%u:%a' '$gazeJournalPath'") { return $fixture.JournalMode }
    if ($Command -eq 'getprop ro.boot.serialno' -or $Command -eq 'getprop ro.serialno') { return $fixture.Serial }
    if ($Command -eq 'cat /proc/sys/kernel/random/boot_id') { return $fixture.BootId }
    if ($Command -eq "getprop $modelProperty") { return $fixture.Property }
    if ($Command -eq 'getprop init.svc.trackingservice') {
        if ($fixture.Running) { return 'running' }
        return 'stopped'
    }
    if ($Command -eq 'cat /proc/mounts') {
        if ($fixture.Mount) { return "$remoteModel $targetModel ext4 rw,relatime 0 0" }
        return ''
    }
    if ($Command -eq "stat -c '%d:%i' '$remoteModel'" -or $Command -eq "sha256sum '$remoteModel'") {
        if (-not $fixture.SourceExists) { throw 'Fixture source missing' }
        if ($Command.StartsWith('stat')) { return $fixture.SourceIdentity }
        return "$($fixture.SourceHash)  $remoteModel"
    }
    if ($Command -eq "stat -c '%d:%i' '$targetModel'") {
        if ($fixture.ForeignMount) { return '999:888' }
        if ($fixture.Mount) { return $fixture.SourceIdentity }
        return '100:300'
    }
    if ($Command -eq "sha256sum '$targetModel'") {
        $hash = if ($fixture.ForeignMount -or $fixture.ModelChanged) { 'c' * 64 }
            elseif ($fixture.Mount) { $fixture.SourceHash } else { $fixture.OriginalHash }
        return "$hash  $targetModel"
    }
    if ($Command.StartsWith('umask 077;')) {
        if ($null -ne $fixture.Journal -or $fixture.Failure -eq 'journal-race') { throw 'Fixture journal already exists' }
        if ($Command -notmatch "printf '%s' '([A-Za-z0-9+/=]+)'" -or
            -not $Command.Contains("chmod 0600 '") -or -not $Command.Contains("&& ln '") -or -not $Command.EndsWith('&& sync')) {
            throw 'Journal was not committed privately and exclusively'
        }
        $fixture.Mutations.Add('journal')
        $fixture.Journal = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Matches[1]))
        return ''
    }
    if ($Command -eq 'stop trackingservice') { $fixture.Mutations.Add('stop'); $fixture.Running = $false; return '' }
    if ($Command -eq 'start trackingservice') { $fixture.Mutations.Add('start'); $fixture.Running = $true; return '' }
    if ($Command -match "^setprop $([regex]::Escape($modelProperty)) '(.*)'$") {
        if ($fixture.Failure -eq 'property') { throw 'Fixture property write failed' }
        $fixture.Mutations.Add('property')
        if (-not $fixture.IgnorePropertyWrite) { $fixture.Property = $Matches[1] }
        return ''
    }
    if ($Command -eq "umount '$targetModel'") {
        if ($fixture.Failure -eq 'unmount') { throw 'Fixture unmount failed' }
        $fixture.Mutations.Add('unmount'); $fixture.Mount = $false; return ''
    }
    if ($Command -eq "rm '$gazeJournalPath'") { $fixture.Mutations.Add('delete-journal'); $fixture.Journal = $null; return '' }
    if ($Command -eq "rm -f '$remoteModel'") { $fixture.Mutations.Add('delete-source'); $fixture.SourceExists = $false; return '' }
    throw "Unexpected fake ADB command: $Command"
}

function Wait-TrackingService {
    if (-not $fixture.Running) { throw 'Fixture tracking service did not restart' }
}

function Test-Recovery([string]$Name, [string]$Outcome, [string]$ErrorContains = '', [switch]$NoMutation) {
    $script:fixtureOutput = @()
    $failure = ''
    try { & { Invoke-GazeRecovery } 6>&1 | ForEach-Object { $script:fixtureOutput += $_.ToString() } }
    catch { $failure = $_.Exception.Message }
    Assert-Equal $true ($fixtureOutput -contains "QPRO_GAZE_RECOVERY $Outcome") "$Name machine outcome"
    Assert-Equal ($Outcome -eq 'unconfirmed') (-not [string]::IsNullOrWhiteSpace($failure)) "$Name failure status"
    if ($ErrorContains -and $failure -notlike "*$ErrorContains*") { throw "$Name lost its useful error: $failure" }
    if ($NoMutation) { Assert-Equal 0 $fixture.Mutations.Count "$Name leaves foreign/unknown state unchanged" }
    Write-Host "PASS: $Name"
}

foreach ($original in @('false', 'true', '', '0', '1')) {
    Reset-Fixture $original
    Save-FixtureJournal
    $fixture.Mount = $true; $fixture.Property = 'true'
    Test-Recovery "Interrupted session restores original '$original'" 'restored'
    Assert-Equal $original $fixture.Property 'Original property preserved'
    Assert-Equal $false $fixture.Mount 'Owned mount removed'
    Assert-Equal $null $fixture.Journal 'Journal removed only after verification'
}

Reset-Fixture; Save-FixtureJournal
$fixture.Property = 'true'; $fixture.BootId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'
Test-Recovery 'Reboot removes mount but persistent property is recovered' 'restored'
Assert-Equal 'false' $fixture.Property 'Reboot property restored'
Assert-Equal $false ($fixture.Mutations -contains 'unmount') 'No blind unmount after reboot'

Reset-Fixture 'true'; Save-FixtureJournal
Test-Recovery 'Reboot preserves pre-existing true without restarting tracking' 'restored'
Assert-Equal 'true' $fixture.Property 'Pre-existing true preserved'
Assert-Equal $false ($fixture.Mutations -contains 'stop') 'No unnecessary tracking restart'

Reset-Fixture
Test-Recovery 'No journal and disabled property reports none' 'none' -NoMutation
Reset-Fixture 'true'
Test-Recovery 'Legacy unjournaled true is not overwritten' 'unconfirmed' 'without a Qpro recovery journal' -NoMutation
Reset-Fixture
$fixture.Mount = $true
Test-Recovery 'Unjournaled mount is not removed' 'unconfirmed' 'without a verified recovery journal' -NoMutation

Reset-Fixture; Save-FixtureJournal
$fixture.Mount = $true; $fixture.ForeignMount = $true; $fixture.Property = 'true'
Test-Recovery 'Foreign mount is not removed' 'unconfirmed' 'foreign eye-model mount' -NoMutation
Reset-Fixture; Save-FixtureJournal
$fixture.Serial = 'ANOTHERQUEST'
Test-Recovery 'Foreign device journal is rejected' 'unconfirmed' 'another headset' -NoMutation
Reset-Fixture; Save-FixtureJournal
$fixture.ModelChanged = $true
Test-Recovery 'Firmware/model replacement is rejected' 'unconfirmed' 'changed since' -NoMutation
Reset-Fixture; Save-FixtureJournal
$fixture.SourceIdentity = '100:201'
Test-Recovery 'Replaced temporary source is rejected' 'unconfirmed' 'changed or is missing' -NoMutation
Reset-Fixture; Save-FixtureJournal
$fixture.SourceExists = $false
Test-Recovery 'Missing source cannot authorize recovery' 'unconfirmed' 'source missing' -NoMutation
Reset-Fixture 'true'; Save-FixtureJournal
$fixture.Property = 'false'
Test-Recovery 'External property change is preserved' 'unconfirmed' 'changed outside' -NoMutation
Reset-Fixture; Save-FixtureJournal
$fixture.JournalMode = '2000:644'
Test-Recovery 'Non-private journal is rejected' 'unconfirmed' 'root-owned' -NoMutation
Reset-Fixture
$fixture.Journal = '{broken'
Test-Recovery 'Malformed JSON is rejected' 'unconfirmed' 'valid JSON' -NoMutation
Reset-Fixture
$journal.targetModel = "/odm/etc/eyetracking/runtime/models/../../unsafe/bolt.ptl"
Save-FixtureJournal
Test-Recovery 'Traversal target is rejected before shell use' 'unconfirmed' 'valid Qpro session' -NoMutation
Reset-Fixture
$journal.remoteModel = "/data/local/tmp/file'; reboot; '"
Save-FixtureJournal
Test-Recovery 'Injected source path is rejected before shell use' 'unconfirmed' 'valid Qpro session' -NoMutation
Reset-Fixture
$journal.ownerHost = 'd' * 64; Save-FixtureJournal
Test-Recovery 'Another PC/user cannot recover an unknown owner' 'unconfirmed' 'another PC or user' -NoMutation

foreach ($operation in @('property', 'unmount')) {
    Reset-Fixture; Save-FixtureJournal
    $fixture.Mount = $true; $fixture.Property = 'true'; $fixture.Failure = $operation
    Test-Recovery "Failed $operation retains journal and restarts service" 'unconfirmed' "Fixture $operation"
    Assert-Equal $true $fixture.Running 'Tracking service restarted after failure'
    Assert-Equal $true ($null -ne $fixture.Journal) 'Recovery record retained for retry'
    Assert-Equal $true $fixture.SourceExists 'Source retained for retry'
}
Reset-Fixture; Save-FixtureJournal
$fixture.Mount = $true; $fixture.Property = 'true'; $fixture.IgnorePropertyWrite = $true
Test-Recovery 'Silent property failure is caught by final verification' 'unconfirmed' 'could not be confirmed'
Assert-Equal $true ($null -ne $fixture.Journal) 'Journal retained after failed verification'

Reset-Fixture ''
New-GazeJournal '' $fixture.SourceHash $fixture.OriginalHash
Assert-Equal $true $cleanupNeeded 'Committed journal enables own cleanup'
$created = $fixture.Journal | ConvertFrom-Json
Assert-Equal '' $created.originalProperty 'Empty original value recorded exactly'
Assert-Equal $false $fixture.Mount 'Journal exists before mount mutation'
Assert-Equal '' $fixture.Property 'Journal creation does not change the property'
Test-Recovery 'Canceled startup before mount restores its own recorded state' 'restored'

Reset-Fixture
$fixture.Failure = 'journal-race'
try { New-GazeJournal 'false' $fixture.SourceHash $fixture.OriginalHash; throw 'Concurrent journal commit was accepted' }
catch { if ($_.Exception.Message -notlike '*journal already exists*') { throw } }
Assert-Equal $false $cleanupNeeded 'Losing journal race does not claim the other session'
Assert-Equal 0 $fixture.Mutations.Count 'Losing journal race does not change tracking'
Write-Host 'PASS: exclusive journal commit refuses a concurrent owner'

Reset-Fixture
$fixture.ModelChanged = $true
try { New-GazeJournal 'false' $fixture.SourceHash $fixture.OriginalHash; throw 'Changed model was accepted' }
catch { if ($_.Exception.Message -notlike '*changed after compatibility checking*') { throw } }
Assert-Equal 0 $fixture.Mutations.Count 'A changed model cannot receive a recovery journal or tracking mutation'
Write-Host 'PASS: journal refuses a model changed after preflight'

$ownerStart = [Diagnostics.ProcessStartInfo]::new()
$ownerStart.FileName = Join-Path $PSHOME 'powershell.exe'
$ownerStart.Arguments = '-NoProfile -NonInteractive -Command "Start-Sleep -Seconds 30"'
$ownerStart.UseShellExecute = $false; $ownerStart.CreateNoWindow = $true
$ownerProcess = [Diagnostics.Process]::Start($ownerStart)
try {
    Reset-Fixture
    $journal.ownerPid = $ownerProcess.Id.ToString()
    $journal.ownerStarted = $ownerProcess.StartTime.ToUniversalTime().Ticks.ToString()
    Save-FixtureJournal
    Test-Recovery 'Another live Qpro owner is not interrupted' 'unconfirmed' 'still running' -NoMutation
} finally {
    if (-not $ownerProcess.HasExited) { $ownerProcess.Kill(); $ownerProcess.WaitForExit() }
    $ownerProcess.Dispose()
}

Write-Host 'PASS: gaze recovery covers interruption, reboot, original values, ownership, concurrent sessions, malformed state and failed restoration.'
