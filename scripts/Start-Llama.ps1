[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Run')][string]$ModelPath,
    [Parameter(ParameterSetName = 'List')][switch]$ListDevices,
    [ValidateSet('Cpu', 'Vulkan')][string]$Backend = 'Vulkan',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidatePattern('^Vulkan[0-9]+$')][string]$Device = 'Vulkan0',
    [ValidatePattern('^(auto|all|[0-9]+)$')][string]$GpuLayers = 'auto',
    [ValidateRange(1, 65535)][int]$Port = 8080,
    [ValidateRange(512, 1048576)][int]$ContextSize = 4096
)

. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-Windows
$binaryDirectory = Get-LlamaBinaryDirectory -Backend $Backend -Configuration $Configuration
$serverPath = Join-Path $binaryDirectory 'llama-server.exe'
if (-not (Test-Path -LiteralPath $serverPath)) {
    throw "llama-server is missing. Run ./scripts/Build-Llama.ps1 -Backend $Backend -Configuration $Configuration."
}

if ($ListDevices) {
    if ($Backend -eq 'Vulkan') {
        $devices = @(Get-LlamaVulkanDevices -ServerPath $serverPath)
        if ($devices.Count -eq 0) {
            throw 'No Vulkan GPU was detected. Check the GPU driver or use -Backend Cpu.'
        }
        $devices
    }
    else {
        Write-Output 'CPU backend selected; GPU offloading is disabled.'
    }
    return
}

if (-not (Test-Path -LiteralPath $ModelPath -PathType Leaf)) {
    throw "Model file was not found: $ModelPath"
}
$resolvedModelPath = (Resolve-Path -LiteralPath $ModelPath).ProviderPath
$serverArguments = @(
    '--model', $resolvedModelPath,
    '--host', '127.0.0.1',
    '--port', "$Port",
    '--ctx-size', "$ContextSize",
    '--no-webui'
)

if ($Backend -eq 'Vulkan') {
    $devices = @(Get-LlamaVulkanDevices -ServerPath $serverPath)
    if ($Device -notin @($devices | ForEach-Object { $_.Name })) {
        throw "GPU $Device is unavailable. Run ./scripts/Start-Llama.ps1 -ListDevices, or use -Backend Cpu."
    }
    $serverArguments += @('--device', $Device, '--gpu-layers', $GpuLayers)
}
else {
    $serverArguments += @('--device', 'none', '--gpu-layers', '0', '--no-kv-offload')
}

Invoke-CheckedCommand $serverPath $serverArguments
