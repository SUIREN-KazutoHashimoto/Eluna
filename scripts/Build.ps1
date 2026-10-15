[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)

. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-Windows
$repositoryRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repositoryRoot
try {
    Invoke-CheckedCommand dotnet @('restore', 'Eluna.slnx', '--configfile', 'NuGet.Config')
    Invoke-CheckedCommand dotnet @('build', 'Eluna.slnx', '--configuration', $Configuration, '--no-restore')
}
finally {
    Pop-Location
}
