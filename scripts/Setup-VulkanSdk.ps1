[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-Windows
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sdkPin = Get-VulkanSdkPin
$sdkDirectory = Get-LocalVulkanSdkPath
$requiredFiles = @('Bin/glslc.exe', 'Include/vulkan/vulkan.hpp', 'Lib/vulkan-1.lib')

$missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $sdkDirectory $_)) })
if ($missingFiles.Count -eq 0) {
    Write-Output "Vulkan SDK $($sdkPin.version) is already available: $sdkDirectory"
    return
}

$downloadDirectory = Join-Path $repositoryRoot '.local/downloads'
New-Item -ItemType Directory -Force -Path $downloadDirectory | Out-Null
$installerPath = Join-Path $downloadDirectory "vulkansdk-windows-X64-$($sdkPin.version).exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    Write-Output "Downloading Vulkan SDK $($sdkPin.version) from LunarG..."
    $previousProgressPreference = $ProgressPreference
    try {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -UseBasicParsing -Uri $sdkPin.installerUrl -OutFile $installerPath
    }
    finally {
        $ProgressPreference = $previousProgressPreference
    }
}

$actualHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
if ($actualHash -ne $sdkPin.sha256) {
    throw "SDK checksum mismatch. Remove the cached installer and retry: $installerPath"
}

$installerArguments = @(
    '--root', ('"' + $sdkDirectory + '"'),
    '--accept-licenses', '--default-answer', '--confirm-command',
    'install', 'copy_only=1'
)
$installerProcess = Start-Process -FilePath $installerPath -ArgumentList $installerArguments -WindowStyle Hidden -PassThru
try {
    $installerProcess.WaitForExit()
    if ($installerProcess.ExitCode -ne 0) {
        throw "Vulkan SDK setup failed with exit code $($installerProcess.ExitCode)."
    }
}
finally {
    $installerProcess.Dispose()
}

foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $sdkDirectory $requiredFile))) {
        throw "Vulkan SDK setup did not provide $requiredFile."
    }
}

Write-Output "Vulkan SDK $($sdkPin.version) ready: $sdkDirectory"
