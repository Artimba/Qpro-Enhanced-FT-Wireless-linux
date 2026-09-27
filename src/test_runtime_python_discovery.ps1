$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'runtime-python.ps1')

# A damaged Python installation can leave a registry entry but no working exe.
# Exercise that discovery branch without reading or changing the real registry.
function Get-Item {
    param([string]$LiteralPath, [string]$ErrorAction)
    if ($LiteralPath -notlike 'Registry::*PythonCore*') { return $null }
    $key = New-Object PSObject
    $key | Add-Member -MemberType ScriptMethod -Name GetValue -Value {
        param([string]$valueName)
        if ($valueName -eq '') { return 'C:\BrokenPython312' }
        return $null
    }
    return $key
}
function Test-QproPython312 { param([string]$Python) return $false }
function Get-Command { param([string]$Name, [string]$ErrorAction) return $null }

$result = Find-QproExistingPython312
if ($null -ne $result) { throw "Expected no working Python, got $result" }
Write-Host 'PASS: broken registered Python does not crash discovery'
