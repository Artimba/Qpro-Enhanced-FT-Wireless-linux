param(
    [ValidateSet('neutral', 'left', 'right', 'both')]
    [string]$Pose,
    [ValidateRange(1, 30)]
    [int]$Seconds = 5
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Core

$map = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting(
    'VirtualDesktop.BodyState',
    [System.IO.MemoryMappedFiles.MemoryMappedFileRights]::Read)
try {
    $view = $map.CreateViewAccessor(
        0, 360, [System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
    try {
        $first = [byte[]]::new(360)
        $second = [byte[]]::new(360)
        $fields = [ordered]@{
            LipCornerPullerL = 32; LipCornerPullerR = 33
            DimplerL = 10; DimplerR = 11
            LipStretcherL = 42; LipStretcherR = 43
            MouthLeft = 53; MouthRight = 54
        }
        $samples = [System.Collections.Generic.List[object]]::new()
        $consistentFrames = 0
        $lastFaceFlag = 0
        $timer = [System.Diagnostics.Stopwatch]::StartNew()
        while ($timer.Elapsed.TotalSeconds -lt $Seconds) {
            [void]$view.ReadArray(0, $first, 0, 360)
            [System.Threading.Thread]::MemoryBarrier()
            [void]$view.ReadArray(0, $second, 0, 360)
            $same = $true
            for ($i = 0; $i -lt 360; $i++) {
                if ($first[$i] -ne $second[$i]) { $same = $false; break }
            }
            if ($same) {
                $consistentFrames++
                $lastFaceFlag = $second[1]
            }
            if ($same -and $second[1] -ne 0) {
                $reading = [ordered]@{}
                foreach ($name in $fields.Keys) {
                    $reading[$name] = [BitConverter]::ToSingle(
                        $second, 4 + 4 * $fields[$name])
                }
                $samples.Add([pscustomobject]$reading)
            }
            Start-Sleep -Milliseconds 50
        }
        if ($samples.Count -eq 0) {
            throw "No valid Virtual Desktop face frames arrived during the sample. Consistent frames: $consistentFrames; latest face flag: $lastFaceFlag."
        }
        $summary = [ordered]@{ pose = $Pose; samples = $samples.Count }
        foreach ($name in $fields.Keys) {
            $stats = $samples | Measure-Object -Property $name -Average -Maximum
            $summary[$name] = [ordered]@{
                mean = [math]::Round($stats.Average, 3)
                max = [math]::Round($stats.Maximum, 3)
            }
        }
        [pscustomobject]$summary | ConvertTo-Json -Depth 4
    }
    finally { $view.Dispose() }
}
finally { $map.Dispose() }
