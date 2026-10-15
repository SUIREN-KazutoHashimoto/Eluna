[CmdletBinding()]
param(
    [ValidateSet('Cpu', 'Vulkan')][string]$Backend = 'Cpu',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateRange(1, 64)][int]$Parallel = 4,
    [string]$VulkanSdkPath,
    [switch]$EnableBackendTests,
    [switch]$StaticRuntime
)

. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-Windows
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourceDirectory = Join-Path $repositoryRoot 'third_party/llama.cpp'
$buildDirectory = Join-Path $repositoryRoot "build/llama-$($Backend.ToLowerInvariant())"
if ($StaticRuntime) {
    $buildDirectory += '-static'
}

if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory 'CMakeLists.txt'))) {
    throw 'llama.cpp is missing. Run: git submodule update --init --recursive'
}

$cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
if ($cmakeCommand) {
    $cmakePath = $cmakeCommand.Source
}
else {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswherePath)) {
        throw 'Install the Visual Studio C++ workload with CMake tools, or add CMake to PATH.'
    }

    $cmakePaths = @(& $vswherePath -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe')
    if ($LASTEXITCODE -ne 0 -or $cmakePaths.Count -eq 0) {
        throw 'CMake was not found. Install the Visual Studio C++ workload with CMake tools.'
    }

    $cmakePath = $cmakePaths[0]
}

$vulkanEnabled = 'OFF'
if ($Backend -eq 'Vulkan') {
    $vulkanEnabled = 'ON'
    if ([string]::IsNullOrWhiteSpace($VulkanSdkPath)) {
        if (-not [string]::IsNullOrWhiteSpace($env:VULKAN_SDK)) {
            $VulkanSdkPath = $env:VULKAN_SDK
        }
        else {
            $VulkanSdkPath = Get-LocalVulkanSdkPath
        }
    }

    $VulkanSdkPath = [IO.Path]::GetFullPath($VulkanSdkPath)
    foreach ($requiredFile in @('Bin/glslc.exe', 'Include/vulkan/vulkan.hpp', 'Lib/vulkan-1.lib')) {
        if (-not (Test-Path -LiteralPath (Join-Path $VulkanSdkPath $requiredFile))) {
            throw "Vulkan SDK is missing $requiredFile. Run ./scripts/Setup-VulkanSdk.ps1, or pass -VulkanSdkPath."
        }
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($VulkanSdkPath)) {
    throw '-VulkanSdkPath can only be used with -Backend Vulkan.'
}

$testsEnabled = 'OFF'
if ($EnableBackendTests) {
    $testsEnabled = 'ON'
}

$configureArguments = @(
    '-S', $sourceDirectory,
    '-B', $buildDirectory,
    '-A', 'x64',
    "-DCMAKE_PROJECT_TOP_LEVEL_INCLUDES=$repositoryRoot/cmake/NativeConfiguration.cmake",
    '-DBUILD_SHARED_LIBS=ON',
    '-DGGML_NATIVE=OFF',
    '-DGGML_CCACHE=OFF',
    '-DGGML_BACKEND_DL=ON',
    '-DGGML_CPU_ALL_VARIANTS=ON',
    '-DGGML_OPENMP=OFF',
    '-DGGML_OPENMP_FETCH=OFF',
    '-DGGML_CUDA=OFF',
    "-DGGML_VULKAN=$vulkanEnabled",
    '-DGGML_RPC=OFF',
    '-DGGML_BLAS=OFF',
    '-DGGML_CPU_KLEIDIAI=OFF',
    "-DLLAMA_BUILD_TESTS=$testsEnabled",
    '-DLLAMA_BUILD_EXAMPLES=OFF',
    '-DLLAMA_BUILD_APP=OFF',
    '-DLLAMA_BUILD_SERVER=ON',
    '-DLLAMA_BUILD_MTMD=ON',
    '-DLLAMA_BUILD_UI=OFF',
    '-DLLAMA_USE_PREBUILT_UI=OFF',
    '-DLLAMA_OPENSSL=OFF',
    '-DLLAMA_SUBPROCESS=OFF',
    '-DLLAMA_LLGUIDANCE=OFF',
    '-DLLAMA_BUILD_BORINGSSL=OFF',
    '-DLLAMA_BUILD_LIBRESSL=OFF',
    '-DFETCHCONTENT_FULLY_DISCONNECTED=ON'
)

if ($Backend -eq 'Vulkan') {
    $configureArguments += @(
        "-DCMAKE_PREFIX_PATH=$VulkanSdkPath",
        "-DVulkan_INCLUDE_DIR=$VulkanSdkPath/Include",
        "-DVulkan_LIBRARY=$VulkanSdkPath/Lib/vulkan-1.lib",
        "-DVulkan_GLSLC_EXECUTABLE=$VulkanSdkPath/Bin/glslc.exe"
    )
}

if ($StaticRuntime) {
    $configureArguments += @(
        '-DCMAKE_POLICY_DEFAULT_CMP0091=NEW',
        '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded$<$<CONFIG:Debug>:Debug>',
        '-DBUILD_SHARED_LIBS=OFF',
        '-DGGML_BACKEND_DL=OFF',
        '-DGGML_CPU_ALL_VARIANTS=OFF',
        '-DGGML_SSE42=OFF',
        '-DGGML_AVX=OFF',
        '-DGGML_AVX2=OFF',
        '-DGGML_FMA=OFF',
        '-DGGML_F16C=OFF',
        '-DGGML_BMI2=OFF'
    )
}

Invoke-CheckedCommand $cmakePath $configureArguments
Invoke-CheckedCommand $cmakePath @('--build', $buildDirectory, '--config', $Configuration, '--target', 'llama-server', '--parallel', "$Parallel")
if ($EnableBackendTests) {
    Invoke-CheckedCommand $cmakePath @('--build', $buildDirectory, '--config', $Configuration, '--target', 'test-backend-ops', '--parallel', "$Parallel")
}

$serverPath = Join-Path $buildDirectory "bin/$Configuration/llama-server.exe"
if (-not (Test-Path -LiteralPath $serverPath)) {
    throw "Build completed, but llama-server was not found at $serverPath."
}

Invoke-CheckedCommand $serverPath @('--version')
Write-Output "llama-server: $serverPath"
