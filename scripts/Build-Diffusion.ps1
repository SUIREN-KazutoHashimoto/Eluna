[CmdletBinding()]
param([ValidateSet('Cpu', 'Vulkan')][string]$Backend = 'Vulkan', [ValidateRange(1, 32)][int]$Parallel = 4)
. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-Windows
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourceDirectory = Join-Path $repositoryRoot 'third_party/stable-diffusion.cpp'
$buildDirectory = Join-Path $repositoryRoot "build/diffusion-$($Backend.ToLowerInvariant())-static"
$expected = '120e3e875c5501c1babb50c0966122cb569d12d4'
if ((& git -C $sourceDirectory rev-parse HEAD) -ne $expected) { throw 'Unexpected stable-diffusion.cpp commit.' }
$cmake = Get-Command cmake -ErrorAction SilentlyContinue
if ($cmake) { $cmakePath = $cmake.Source }
else {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $paths = @(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe')
    if ($paths.Count -eq 0) { throw 'MSVC and CMake are required.' }
    $cmakePath = $paths[0]
}
$vulkan = if ($Backend -eq 'Vulkan') { 'ON' } else { 'OFF' }
$arguments = @('-S', $sourceDirectory, '-B', $buildDirectory, '-A', 'x64',
    "-DCMAKE_PROJECT_TOP_LEVEL_INCLUDES=$repositoryRoot/cmake/NativeConfiguration.cmake",
    '-DCMAKE_POLICY_DEFAULT_CMP0091=NEW', '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded',
    '-DBUILD_SHARED_LIBS=OFF', '-DSD_BUILD_SHARED_LIBS=OFF', '-DSD_BUILD_SHARED_GGML_LIB=OFF',
    '-DSD_BUILD_EXAMPLES=ON', '-DSD_WEBP=OFF', '-DSD_WEBM=OFF', "-DSD_VULKAN=$vulkan",
    '-DGGML_NATIVE=OFF', '-DGGML_OPENMP=OFF', '-DGGML_BACKEND_DL=OFF', '-DGGML_CCACHE=OFF',
    '-DGGML_AVX=OFF', '-DGGML_AVX2=OFF', '-DGGML_FMA=OFF', '-DGGML_F16C=OFF', '-DFETCHCONTENT_FULLY_DISCONNECTED=ON')
if ($Backend -eq 'Vulkan') {
    $sdk = Get-LocalVulkanSdkPath
    $arguments += @("-DCMAKE_PREFIX_PATH=$sdk", "-DVulkan_INCLUDE_DIR=$sdk/Include", "-DVulkan_LIBRARY=$sdk/Lib/vulkan-1.lib", "-DVulkan_GLSLC_EXECUTABLE=$sdk/Bin/glslc.exe")
}
Invoke-CheckedCommand $cmakePath $arguments
Invoke-CheckedCommand $cmakePath @('--build', $buildDirectory, '--config', 'Release', '--target', 'sd-cli', '--parallel', "$Parallel")
$binary = Join-Path $buildDirectory 'bin/Release/sd-cli.exe'
if (-not (Test-Path -LiteralPath $binary)) { throw 'sd-cli build output is missing.' }
Write-Output $binary
