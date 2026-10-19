[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -STA -File $PSCommandPath
    exit $LASTEXITCODE
}
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$taskAssetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Eluna.App/Assets'))
$taskFrames = [Collections.Generic.List[byte[]]]::new()
$taskSizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
foreach ($taskSize in $taskSizes) {
    $taskVisual = [Windows.Media.DrawingVisual]::new()
    $taskDrawing = $taskVisual.RenderOpen()
    $taskDrawing.PushTransform([Windows.Media.ScaleTransform]::new($taskSize / 128.0, $taskSize / 128.0))
    $taskPurple = [Windows.Media.BrushConverter]::new().ConvertFromString('#5865f2')
    $taskWhite = [Windows.Media.Brushes]::White
    $taskDrawing.DrawRoundedRectangle($taskPurple, $null, [Windows.Rect]::new(0, 0, 128, 128), 30, 30)
    $taskDrawing.DrawEllipse($taskWhite, $null, [Windows.Point]::new(60, 61), 39, 39)
    $taskDrawing.DrawGeometry($taskWhite, $null, [Windows.Media.Geometry]::Parse('M37,84 L29,107 L61,96 Z'))
    $taskDrawing.DrawEllipse($taskPurple, $null, [Windows.Point]::new(75, 47), 33, 33)
    $taskDrawing.DrawGeometry($taskWhite, $null, [Windows.Media.Geometry]::Parse('M94,28 L97.5,35.5 L105,39 L97.5,42.5 L94,50 L90.5,42.5 L83,39 L90.5,35.5 Z'))
    $taskDrawing.DrawEllipse($taskWhite, $null, [Windows.Point]::new(106, 65), 3.5, 3.5)
    $taskDrawing.Pop(); $taskDrawing.Close()
    $taskBitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($taskSize, $taskSize, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $taskBitmap.Render($taskVisual)
    $taskEncoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $taskEncoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($taskBitmap))
    $taskBuffer = [IO.MemoryStream]::new(); $taskEncoder.Save($taskBuffer)
    $taskFrames.Add($taskBuffer.ToArray()); $taskBuffer.Dispose()
}
$taskIco = [IO.MemoryStream]::new(); $taskWriter = [IO.BinaryWriter]::new($taskIco)
$taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskSizes.Count)
$taskOffset = 6 + 16 * $taskSizes.Count
for ($taskIndex = 0; $taskIndex -lt $taskSizes.Count; $taskIndex++) {
    $taskDimension = if ($taskSizes[$taskIndex] -eq 256) { 0 } else { $taskSizes[$taskIndex] }
    $taskWriter.Write([byte]$taskDimension); $taskWriter.Write([byte]$taskDimension)
    $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
    $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32)
    $taskWriter.Write([uint32]$taskFrames[$taskIndex].Length); $taskWriter.Write([uint32]$taskOffset)
    $taskOffset += $taskFrames[$taskIndex].Length
}
foreach ($taskFrame in $taskFrames) { $taskWriter.Write($taskFrame) }
$taskWriter.Flush(); [IO.File]::WriteAllBytes((Join-Path $taskAssetRoot 'Eluna.ico'), $taskIco.ToArray())
[IO.File]::WriteAllBytes((Join-Path $taskAssetRoot 'Eluna.png'), $taskFrames[$taskFrames.Count - 1])
$taskWriter.Dispose(); $taskIco.Dispose()
Write-Output 'Generated the original Eluna crescent conversation icon in ten Windows sizes.'
