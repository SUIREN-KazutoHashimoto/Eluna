Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Windows {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw 'Eluna supports Windows only.'
    }
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

function Get-VulkanSdkPin {
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'vulkan-sdk.json') -Raw | ConvertFrom-Json
}

function Get-LocalVulkanSdkPath {
    $sdkPin = Get-VulkanSdkPin
    Join-Path (Split-Path -Parent $PSScriptRoot) ".local/vulkan-sdk/$($sdkPin.version)"
}

function Get-LlamaBinaryDirectory {
    param(
        [ValidateSet('Cpu', 'Vulkan')][string]$Backend,
        [ValidateSet('Debug', 'Release')][string]$Configuration
    )

    Join-Path (Split-Path -Parent $PSScriptRoot) "build/llama-$($Backend.ToLowerInvariant())/bin/$Configuration"
}

function Get-LlamaVulkanDevices {
    param([Parameter(Mandatory = $true)][string]$ServerPath)

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $ServerPath
    $startInfo.Arguments = '--list-devices'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        [void]$process.Start()
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill()
            throw 'GPU enumeration timed out after 30 seconds.'
        }

        $outputText = $outputTask.GetAwaiter().GetResult()
        $errorText = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "GPU enumeration failed: $errorText"
        }

        if (-not [string]::IsNullOrWhiteSpace($errorText)) {
            Write-Verbose $errorText
        }
        $seenDevices = @{}
        foreach ($deviceMatch in [regex]::Matches($outputText, '(?m)^\s*(Vulkan\d+):\s*(.+)$')) {
            if ($seenDevices.ContainsKey($deviceMatch.Groups[1].Value)) { continue }
            $seenDevices[$deviceMatch.Groups[1].Value] = $true
            [pscustomobject]@{
                Name = $deviceMatch.Groups[1].Value
                Description = $deviceMatch.Groups[2].Value.Trim()
            }
        }
    }
    finally {
        $process.Dispose()
    }
}
