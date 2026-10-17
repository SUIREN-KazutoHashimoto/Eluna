[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-Windows
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskCmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
if ($taskCmakeCommand) { $taskCmake = $taskCmakeCommand.Source }
else {
    $taskVswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path -LiteralPath $taskVswhere)) { throw 'The existing Visual Studio C++ and Windows SDK tools are required to build the UI renderer.' }
    $taskCandidates = @(& $taskVswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe')
    if ($taskCandidates.Count -eq 0) { throw 'CMake is missing from the existing C++ build tools.' }
    $taskCmake = $taskCandidates[0]
}
$taskSource = Join-Path $taskRepository 'native/Eluna.Text'
$taskBuild = Join-Path $taskRepository 'build/ui'
Invoke-CheckedCommand $taskCmake @('-S', $taskSource, '-B', $taskBuild, '-A', 'x64')
Invoke-CheckedCommand $taskCmake @('--build', $taskBuild, '--config', $Configuration, '--parallel', '2')
