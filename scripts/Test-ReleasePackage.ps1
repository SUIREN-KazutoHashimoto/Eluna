[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory)
. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-Windows
$taskRoot = [IO.Path]::GetFullPath($Directory)
$taskManifest = Get-Content -LiteralPath (Join-Path $taskRoot 'update-manifest.json') -Raw | ConvertFrom-Json
if ($taskManifest.schema -ne 1 -or $taskManifest.dataSchema -ne 5 -or $taskManifest.file -ne 'Eluna.exe' -or $taskManifest.train -notin @('stable','develop')) { throw 'Invalid release manifest.' }
$taskExe = Join-Path $taskRoot 'Eluna.exe'
if ((Get-Item -LiteralPath $taskExe).Length -ne $taskManifest.size -or (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash -ne $taskManifest.sha256) { throw 'Executable hash/size mismatch.' }
if ((Get-FileHash -LiteralPath (Join-Path $taskRoot 'Eluna-Setup.exe') -Algorithm SHA256).Hash -ne $taskManifest.sha256) { throw 'Installer and portable payload differ.' }
$taskProductVersion = (Get-Item -LiteralPath $taskExe).VersionInfo.ProductVersion.Split('+')[0]
if ($taskProductVersion -ne $taskManifest.version) { throw 'Executable version differs from manifest.' }
foreach ($taskLine in Get-Content -LiteralPath (Join-Path $taskRoot 'SHA256SUMS.txt')) {
    if ($taskLine -notmatch '^([a-fA-F0-9]{64})  (Eluna\.exe|Eluna-Setup\.exe|update-manifest\.json)$') { throw 'Invalid checksum entry.' }
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot $Matches[2]) -Algorithm SHA256).Hash -ne $Matches[1]) { throw 'Checksum mismatch.' }
}
Write-Output "Release payload verified: $($taskManifest.version) / $($taskManifest.train)"
