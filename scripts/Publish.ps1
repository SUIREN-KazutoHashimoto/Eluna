[CmdletBinding()]
param([switch]$SkipNativeBuild, [string]$OutputDirectory = 'artifacts/release')

. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-Windows
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipNativeBuild) {
    & (Join-Path $PSScriptRoot 'Build-Llama.ps1') -Backend Cpu -StaticRuntime
    & (Join-Path $PSScriptRoot 'Build-Llama.ps1') -Backend Vulkan -StaticRuntime
    & (Join-Path $PSScriptRoot 'Build-Diffusion.ps1') -Backend Cpu
    & (Join-Path $PSScriptRoot 'Build-Diffusion.ps1') -Backend Vulkan
}
foreach ($backend in @('cpu','vulkan')) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot "build/llama-$backend-static/bin/Release/llama-server.exe"))) {
        throw "The $backend static-runtime native build is missing. Run Publish.ps1 without -SkipNativeBuild."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot "build/diffusion-$backend-static/bin/Release/sd-cli.exe"))) { throw "The $backend image generation engine is missing. Run Build-Diffusion.ps1." }
}

Push-Location $repositoryRoot
try {
    $project = 'src/Eluna.App/Eluna.App.csproj'
    Invoke-CheckedCommand dotnet @('restore', $project, '--runtime', 'win-x64', '--configfile', 'NuGet.Config', '-p:SelfContained=true', '-p:PublishSingleFile=true')
    $workDirectory = Join-Path $repositoryRoot ('artifacts/package-work/' + [Guid]::NewGuid().ToString('N'))
    $payloadDirectory = Join-Path $workDirectory 'native'
    $licenseDirectory = Join-Path $payloadDirectory 'Licenses'
    New-Item -ItemType Directory -Force -Path $licenseDirectory | Out-Null
    foreach ($backend in @('cpu','vulkan')) {
        $backendDirectory = Join-Path $payloadDirectory $backend
        New-Item -ItemType Directory -Force -Path $backendDirectory | Out-Null
        Copy-Item -LiteralPath (Join-Path $repositoryRoot "build/llama-$backend-static/bin/Release/llama-server.exe") -Destination $backendDirectory
        Copy-Item -LiteralPath (Join-Path $repositoryRoot "build/diffusion-$backend-static/bin/Release/sd-cli.exe") -Destination $backendDirectory
    }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $licenseDirectory 'Eluna-Apache-2.0.txt')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'third_party/llama.cpp/LICENSE') -Destination (Join-Path $licenseDirectory 'llama.cpp-MIT.txt')
    $diffusionRoot = Join-Path $repositoryRoot 'third_party/stable-diffusion.cpp'
    foreach ($notice in @('LICENSE', 'ggml/LICENSE', 'thirdparty/LICENSE.darts_clone.txt', 'thirdparty/utf8proc/LICENSE.md', 'thirdparty/oniguruma/COPYING', 'thirdparty/zip.c', 'thirdparty/zip.h', 'thirdparty/miniz.h', 'thirdparty/stb_image.h', 'thirdparty/stb_image_write.h', 'thirdparty/stb_image_resize.h', 'thirdparty/json.hpp')) {
        $source = Join-Path $diffusionRoot $notice
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $licenseDirectory ('stable-diffusion-' + ($notice -replace '[/\\]', '-'))) }
    }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs/notices/stable-diffusion-pytorch-rng-LICENSE.txt') -Destination $licenseDirectory
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'third_party/llama.cpp/vendor') -Recurse -File |
        Where-Object { $_.Name -match '^LICENSE|^COPYING|^NOTICE' } | ForEach-Object {
            $relativeName = $_.FullName.Substring((Join-Path $repositoryRoot 'third_party/llama.cpp/vendor').Length + 1)
            $licenseTarget = Join-Path $licenseDirectory ('vendor/' + $relativeName)
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $licenseTarget) | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $licenseTarget
        }
    $sdkLicense = Join-Path (Get-LocalVulkanSdkPath) 'Licenses/LICENSE.txt'
    if (-not (Test-Path -LiteralPath $sdkLicense)) {
        throw 'Vulkan SDK license notices are missing. Run Setup-VulkanSdk.ps1 first.'
    }
    Copy-Item -LiteralPath $sdkLicense -Destination (Join-Path $licenseDirectory 'Vulkan-SDK-notices.txt')
    $assets = Get-Content -LiteralPath 'src/Eluna.App/obj/project.assets.json' -Raw | ConvertFrom-Json
    foreach ($packageRoot in $assets.packageFolders.PSObject.Properties.Name) {
        foreach ($package in @('Microsoft.NETCore.App.Runtime.win-x64','Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
            $runtimePackageRoot = Join-Path $packageRoot $package.ToLowerInvariant()
            if (Test-Path -LiteralPath $runtimePackageRoot) {
              foreach ($versionDirectory in (Get-ChildItem -LiteralPath $runtimePackageRoot -Directory)) {
                $packageDirectory = $versionDirectory.FullName
                foreach ($notice in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
                    $noticePath = Join-Path $packageDirectory $notice
                    if (Test-Path -LiteralPath $noticePath) {
                        Copy-Item -LiteralPath $noticePath -Destination (Join-Path $licenseDirectory ($package + '-' + $versionDirectory.Name + '-' + $notice))
                    }
                }
              }
            }
        }
    }
    $bundlePath = Join-Path $workDirectory 'NativeRuntime.zip'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($payloadDirectory, $bundlePath)
    $publishDirectory = Join-Path $workDirectory 'publish'
    Invoke-CheckedCommand dotnet @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true',
        "-p:NativeRuntimeBundlePath=$bundlePath", '--output', $publishDirectory)
    $releaseDirectory = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
        [IO.Path]::GetFullPath($OutputDirectory)
    } else {
        [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
    }
    New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
    $executable = Join-Path $releaseDirectory 'Eluna.exe'
    Copy-Item -LiteralPath (Join-Path $publishDirectory 'Eluna.exe') -Destination $executable -Force
    Get-FileHash -LiteralPath $executable -Algorithm SHA256 | Format-List
    Write-Output "Portable prototype: $executable"
}
finally {
    Pop-Location
}
