[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExePath,
    [Parameter(Mandatory)][string]$ModelMetadata,
    [string]$OutputDirectory = ('artifacts/recovery-' + [guid]::NewGuid().ToString('N'))
)

. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-Windows
$appExe = (Resolve-Path -LiteralPath $ExePath).Path
$modelFile = (Resolve-Path -LiteralPath $ModelMetadata).Path
$testRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (@('state.json', 'state-knowledge.json', 'state-skills.json') | Where-Object { Test-Path -LiteralPath (Join-Path $testRoot $_) }) { throw 'Choose a fresh test directory; existing history is preserved.' }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$launchedApp = $null
$recoveryApp = $null
try {
    $launchedApp = Start-Process -FilePath $appExe -ArgumentList @('--data-dir', ('"' + $testRoot + '"'), '--verify-model', ('"' + $modelFile + '"'), '--verify-interruption') -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $state = $null
    do {
        if ($launchedApp.HasExited) { throw 'The app exited before a streaming checkpoint was available.' }
        $statePath = @('state-skills.json', 'state-knowledge.json', 'state.json') | ForEach-Object { Join-Path $testRoot $_ } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if ($statePath -and (Test-Path -LiteralPath $statePath)) {
            try { $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } catch { $state = $null }
        }
        $partial = @()
        if ($null -ne $state) { $partial = @($state.conversations | ForEach-Object { $_.messages } | Where-Object { $_.incomplete -and $_.content.Length -gt 50 }) }
        if ($partial.Count -eq 0) { Start-Sleep -Milliseconds 100 }
    } while ($partial.Count -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($partial.Count -eq 0) { throw 'No streaming checkpoint was saved within 60 seconds.' }
    Copy-Item -LiteralPath $statePath -Destination (Join-Path $testRoot 'state-before-interruption.json')
    $engineIds = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $($launchedApp.Id)" | Where-Object { $_.Name -eq 'llama-server.exe' } | Select-Object -ExpandProperty ProcessId)
    if ($engineIds.Count -eq 0) { throw 'No owned inference engine was running.' }
    # Only terminate the exact process this script launched, after its own checkpoint is saved.
    Stop-Process -Id $launchedApp.Id -Force
    $launchedApp.WaitForExit()
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $remaining = @($engineIds | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
        if ($remaining.Count -gt 0) { Start-Sleep -Milliseconds 100 }
    } while ($remaining.Count -gt 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($remaining.Count -gt 0) { throw 'The Windows job left an inference engine running after app termination.' }
    $recoveryApp = Start-Process -FilePath $appExe -ArgumentList @('--data-dir', ('"' + $testRoot + '"'), '--verify-recovery') -PassThru -WindowStyle Hidden
    if (-not $recoveryApp.WaitForExit(60000)) { throw 'The fresh app startup did not finish recovery.' }
    $report = Get-Content -LiteralPath (Join-Path $testRoot 'recovery.json') -Raw | ConvertFrom-Json
    if (-not $report.success -or $recoveryApp.ExitCode -ne 0) { throw "Recovery failed: $($report.error)" }
    $report | Add-Member -NotePropertyName ownedEngineExited -NotePropertyValue $true
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'result.json') -Encoding utf8
    Write-Output "Forced app termination, engine cleanup, saved output and fresh-start recovery: OK ($testRoot)"
}
finally {
    foreach ($ownedProcess in @($launchedApp, $recoveryApp)) {
        if ($null -ne $ownedProcess -and -not $ownedProcess.HasExited) { Stop-Process -Id $ownedProcess.Id -Force }
    }
}
