[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidatePattern('^Vulkan[0-9]+$')][string]$Device = 'Vulkan0',
    [switch]$StaticRuntime
)

. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-Windows
$binaryDirectory = Get-LlamaBinaryDirectory -Backend Vulkan -Configuration $Configuration
if ($StaticRuntime) { $binaryDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) "build/llama-vulkan-static/bin/$Configuration" }
$serverPath = Join-Path $binaryDirectory 'llama-server.exe'
$testPath = Join-Path $binaryDirectory 'test-backend-ops.exe'
if (-not (Test-Path -LiteralPath $serverPath) -or -not (Test-Path -LiteralPath $testPath)) {
    throw 'Build GPU tests first: ./scripts/Build-Llama.ps1 -Backend Vulkan -EnableBackendTests'
}
if ($StaticRuntime) {
    $isolated = Join-Path (Split-Path -Parent $PSScriptRoot) ('artifacts/gpu-static-test/' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $isolated | Out-Null
    Copy-Item -LiteralPath $serverPath -Destination $isolated
    Copy-Item -LiteralPath $testPath -Destination $isolated
    $serverPath = Join-Path $isolated 'llama-server.exe'
    $testPath = Join-Path $isolated 'test-backend-ops.exe'
}

$devices = @(Get-LlamaVulkanDevices -ServerPath $serverPath)
$selectedDevice = @($devices | Where-Object { $_.Name -eq $Device })
if ($selectedDevice.Count -ne 1) {
    throw "GPU $Device was not detected. No GPU tests were run."
}
Write-Output "Testing $Device - $($selectedDevice[0].Description)"

$testGroups = @(
    @{
        Operations = 'ADD,MUL'
        Parameters = '^type=(f32|f16),ne=\[10,5,4,3\],nr=\[1,1,1,1\],nf=1,perm1=0,src_overlap=0'
    },
    @{
        Operations = 'MUL_MAT'
        Parameters = '^type_a=(f32|f16|q4_0|q4_K|q8_0),type_b=f32,m=16,n=(1|4),k=256,bs=\[1,1\],nr=\[1,1\],per=\[0,1,2,3\],k_v=0,'
    }
)

foreach ($testGroup in $testGroups) {
    $testArguments = @('test', '-b', $Device, '-o', $testGroup.Operations, '-p', $testGroup.Parameters, '--output', 'csv')
    $resultText = & $testPath @testArguments
    if ($LASTEXITCODE -ne 0) {
        $resultText | Write-Output
        throw "GPU correctness tests failed with exit code $LASTEXITCODE."
    }

    $results = @($resultText | ConvertFrom-Csv | Where-Object { $_.backend_name -eq $Device })
    foreach ($operation in $testGroup.Operations.Split(',')) {
        $operationResults = @($results | Where-Object { $_.op_name -eq $operation -and $_.supported -in @('true', '1') })
        if ($operationResults.Count -eq 0) {
            throw "No supported $operation tests were executed on $Device."
        }
        if (@($operationResults | Where-Object { -not [string]::IsNullOrWhiteSpace($_.error_message) }).Count -ne 0) {
            $operationResults | Format-Table | Out-String | Write-Output
            throw "$operation did not match the CPU reference on $Device."
        }
        Write-Output "$operation : $($operationResults.Count) cases passed against the CPU reference."
        if ($operation -eq 'MUL_MAT') {
            foreach ($matrixType in @('f32', 'f16', 'q4_0', 'q4_K', 'q8_0')) {
                if (@($operationResults | Where-Object { $_.op_params.StartsWith("type_a=$matrixType,") }).Count -eq 0) {
                    throw "No supported $matrixType matrix multiplication tests were executed."
                }
            }
        }
    }
}
