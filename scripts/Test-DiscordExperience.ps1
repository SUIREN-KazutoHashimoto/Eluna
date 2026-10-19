[CmdletBinding()]
param(
    [string]$ExecutablePath = 'src/Eluna.App/bin/Release/net10.0-windows/Eluna.exe',
    [string]$OutputDirectory = 'artifacts/experience-final',
    [string]$ModelMetadata,
    [switch]$Portable
)

. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-Windows
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
function Resolve-TaskPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}
$taskExe = Resolve-TaskPath $ExecutablePath
$taskOutput = Resolve-TaskPath $OutputDirectory
if (-not (Test-Path -LiteralPath $taskExe -PathType Leaf)) { throw 'Build or publish Eluna before running this script.' }
if (Test-Path -LiteralPath $taskOutput) { throw 'Use a new output directory so existing fixtures and user data stay untouched.' }
if ($ModelMetadata) { $ModelMetadata = Resolve-TaskPath $ModelMetadata; if (-not (Test-Path -LiteralPath $ModelMetadata -PathType Leaf)) { throw 'Model metadata is missing.' } }
New-Item -ItemType Directory -Path $taskOutput | Out-Null
if ($Portable) {
    $taskApp = Join-Path $taskOutput 'app'
    New-Item -ItemType Directory -Path $taskApp | Out-Null
    Copy-Item -LiteralPath $taskExe -Destination (Join-Path $taskApp 'Eluna.exe')
    $taskExe = Join-Path $taskApp 'Eluna.exe'
    if (@(Get-ChildItem -LiteralPath $taskApp -File).Count -ne 1) { throw 'Portable fixture must start with only Eluna.exe.' }
}
$taskReport = [Collections.Generic.List[object]]::new()
foreach ($taskKind in @('navigation', 'composer', 'messages', 'discord', 'social', 'agents', 'voice', 'polish', 'preview')) {
    $taskCaseOutput = Join-Path $taskOutput $taskKind
    $taskStart = [Diagnostics.ProcessStartInfo]::new($taskExe)
    $taskStart.UseShellExecute = $false
    $taskStart.CreateNoWindow = $true
    $taskStart.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $taskStart.WorkingDirectory = if ($Portable) { Split-Path -Parent $taskExe } else { $repositoryRoot }
    $taskFlag = switch ($taskKind) { 'navigation' { '--verify-experience' } 'messages' { '--verify-message-experience' } { $_ -in @('polish','preview') } { '--capture-dir' } default { '--verify-' + $taskKind } }
    $taskStart.ArgumentList.Add($taskFlag)
    $taskStart.ArgumentList.Add($taskCaseOutput)
    $taskStart.ArgumentList.Add('--data-dir')
    $taskStart.ArgumentList.Add((Join-Path $taskCaseOutput 'data'))
    if ($taskKind -eq 'polish') { $taskStart.ArgumentList.Add('--verify-ui-ux') }
    if ($ModelMetadata -and $taskKind -in @('social','agents')) { $taskStart.ArgumentList.Add('--model-meta'); $taskStart.ArgumentList.Add($ModelMetadata) }
    if ($Portable) {
        $taskStart.Environment['DOTNET_ROOT'] = Join-Path $taskOutput 'absent-runtime'
        $taskStart.Environment['DOTNET_ROOT_X64'] = $taskStart.Environment['DOTNET_ROOT']
        $taskStart.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
        $taskStart.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $taskOutput 'extracted'
    }
    $taskWatch = [Diagnostics.Stopwatch]::StartNew()
    $taskProcess = [Diagnostics.Process]::Start($taskStart)
    $taskLastStage = ''
    try {
        while (-not $taskProcess.WaitForExit(500)) {
            if ($taskWatch.Elapsed.TotalMinutes -gt 11) { $taskProcess.Kill($true); throw "$taskKind timed out." }
            $taskProgress = Join-Path $taskCaseOutput 'progress.json'
            if (Test-Path -LiteralPath $taskProgress) {
                try { $taskStage = (Get-Content -LiteralPath $taskProgress -Raw | ConvertFrom-Json).stage; if ($taskStage -and $taskStage -ne $taskLastStage) { Write-Output "$taskKind : $taskStage"; $taskLastStage = $taskStage } } catch { }
            }
        }
        $taskResultPath = Join-Path $taskCaseOutput 'result.txt'
        $taskResult = if (Test-Path -LiteralPath $taskResultPath) { Get-Content -LiteralPath $taskResultPath -Raw } else { '' }
        if ($taskProcess.ExitCode -ne 0 -or $taskResult -notmatch ': OK') { throw "$taskKind failed (exit $($taskProcess.ExitCode)): $taskResult" }
        Write-Output "$taskKind : OK ($([Math]::Round($taskWatch.Elapsed.TotalSeconds, 1))s)"
        $taskReport.Add([pscustomobject]@{ Case = $taskKind; Seconds = $taskWatch.Elapsed.TotalSeconds; Result = $taskResult.Trim() })
        $taskReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskOutput 'results.json') -Encoding utf8
    }
    finally { $taskProcess.Dispose() }
}
if ($Portable) {
    $taskRuntime = Get-ChildItem -LiteralPath (Join-Path $taskOutput 'navigation/data/Runtime') -Directory | Select-Object -First 1
    if ($null -eq $taskRuntime) { throw 'No bundled native engines were extracted.' }
    $taskNative = foreach ($taskBackend in @('cpu','vulkan')) {
        foreach ($taskName in @('llama-server.exe','sd-cli.exe')) {
            $taskBinary = Join-Path $taskRuntime.FullName "$taskBackend/$taskName"
            $taskVersion = & $taskBinary --version 2>&1
            if ($LASTEXITCODE -ne 0) { throw "$taskBackend/$taskName failed to start." }
            [pscustomobject]@{ Backend = $taskBackend; Name = $taskName; Sha256 = (Get-FileHash -LiteralPath $taskBinary -Algorithm SHA256).Hash; Version = ($taskVersion -join "`n") }
        }
    }
    $taskNative | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskOutput 'native.json') -Encoding utf8
}
Get-FileHash -LiteralPath $taskExe -Algorithm SHA256 | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'exe.json') -Encoding utf8
Write-Output 'Discord experience regression: OK'
