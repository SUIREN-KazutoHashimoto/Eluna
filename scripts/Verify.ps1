[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-Windows
$repositoryRoot = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release

Push-Location $repositoryRoot
try {
    Invoke-CheckedCommand dotnet @('format', 'Eluna.slnx', '--verify-no-changes', '--no-restore')
    Invoke-CheckedCommand dotnet @('run', '--project', 'tests/Eluna.Checks', '-c', 'Release', '--no-build', '--', 'artifacts/checks')
    Invoke-CheckedCommand git @('diff', '--check')
    Invoke-CheckedCommand git @('diff', '--cached', '--check')
}
finally {
    Pop-Location
}
