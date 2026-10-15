using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using Eluna.App.Models;

namespace Eluna.App.Services;

public static class AttachmentService
{
    public const string Filter = "画像・文書|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.pdf;*.docx;*.xlsx;*.pptx;*.txt;*.md;*.csv;*.json;*.log|すべて|*.*";
    public static async Task<List<ChatAttachment>> ReadAsync(string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) { throw new InvalidDataException("添付は1ファイル32 MBまでです。"); }
        string ext = Path.GetExtension(path).ToLowerInvariant(), name = Path.GetFileName(path);
        if (ext == ".pdf") { return await ReadPdfAsync(path, token); }
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }.Contains(ext)) { return new List<ChatAttachment> { Image(path, name) }; }
            string text;
            if (ext is ".docx" or ".xlsx" or ".pptx") { text = ReadOffice(path, ext); }
            else if (new[] { ".txt", ".md", ".csv", ".json", ".log" }.Contains(ext)) { text = File.ReadAllText(path, Encoding.UTF8); }
            else { throw new InvalidDataException("対応形式は画像、PDF、DOCX、XLSX、PPTX、UTF-8のTXT/MD/CSV/JSON/LOGです。"); }
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text)) { throw new InvalidDataException("文書からテキストを読み取れませんでした。画像として添付する方法も使えます。"); }
            if (text.Length > 60000) { text = text[..60000] + "\n［読み取り上限: 先頭60,000文字。以降は未読です］"; }
            return new List<ChatAttachment> { new(name, "document", text) };
        }, token);
    }
    internal static ChatAttachment Image(string path, string name)
    {
        using var input = File.OpenRead(path);
        BitmapDecoder decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        int width = decoder.Frames[0].PixelWidth, height = decoder.Frames[0].PixelHeight;
        if ((long)width * height > 100000000 || width < 1 || height < 1) { throw new InvalidDataException("画像の解像度が大きすぎます。"); }
        input.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.StreamSource = input; image.DecodePixelWidth = Math.Max(1, (int)(width * Math.Min(1, 1280d / Math.Max(width, height)))); image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit(); image.Freeze();
        if (image.PixelHeight > 8192 || image.PixelWidth > 8192) { throw new InvalidDataException("画像が縦長すぎます。分割して添付してください。"); }
        using var output = new MemoryStream();
        var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int alpha = pixels[i + 3];
            for (int j = 0; j < 3; j++) { pixels[i + j] = (byte)((pixels[i + j] * alpha + 255 * (255 - alpha)) / 255); }
            pixels[i + 3] = 255;
        }
        BitmapSource flattened = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, converted.PixelWidth * 4);
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 }; encoder.Frames.Add(BitmapFrame.Create(flattened)); encoder.Save(output);
        if (output.Length > 2 * 1024 * 1024) { throw new InvalidDataException("画像を小さくして再度添付してください。"); }
        return new(name, "image", ImageBase64: Convert.ToBase64String(output.ToArray()));
    }
    internal static string ReadOffice(string path, string extension)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 4096 || archive.Entries.Sum(e => e.Length) > 128 * 1024 * 1024) { throw new InvalidDataException("展開後の文書が大きすぎます。必要な部分を分けて添付してください。"); }
        XDocument Xml(ZipArchiveEntry entry)
        {
            if (entry.Length > 16 * 1024 * 1024) { throw new InvalidDataException("文書内のXMLが大きすぎます。"); }
            using Stream stream = entry.Open(); using XmlReader reader = XmlReader.Create(stream, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 }); return XDocument.Load(reader);
        }
        if (extension == ".docx")
        {
            ZipArchiveEntry entry = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("DOCXの本文がありません。");
            return string.Join('\n', Xml(entry).Descendants().Where(e => e.Name.LocalName == "p").Select(p => string.Concat(p.Descendants().Where(e => e.Name.LocalName is "t" or "tab" or "br").Select(e => e.Name.LocalName == "t" ? e.Value : " "))));
        }
        if (extension == ".pptx")
        {
            if (archive.Entries.Count(e => Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$")) > 80) { throw new InvalidDataException("PPTXは80スライドまでです。"); }
            return string.Join("\n\n", archive.Entries.Where(e => Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$")).OrderBy(e => int.Parse(Regex.Match(e.Name, @"\d+").Value)).Take(80)
                .Select(e => "スライド " + Regex.Match(e.Name, @"\d+").Value + "\n" + string.Join('\n', Xml(e).Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value))));
        }
        string[] shared = archive.GetEntry("xl/sharedStrings.xml") is ZipArchiveEntry strings ? Xml(strings).Descendants().Where(e => e.Name.LocalName == "si").Select(e => string.Concat(e.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value))).ToArray() : [];
        var sheets = new List<string>();
        foreach (ZipArchiveEntry entry in archive.Entries.Where(e => Regex.IsMatch(e.FullName, @"^xl/worksheets/sheet\d+\.xml$")).OrderBy(e => e.FullName).Take(32))
        {
            var rows = new List<string>();
            foreach (XElement row in Xml(entry).Descendants().Where(e => e.Name.LocalName == "row").Take(2000))
            {
                rows.Add(string.Join("\t", row.Elements().Where(e => e.Name.LocalName == "c").Select(cell =>
                {
                    string value = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? string.Concat(cell.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
                    return cell.Attribute("r")?.Value + "=" + (cell.Attribute("t")?.Value == "s" && int.TryParse(value, out int index) && index >= 0 && index < shared.Length ? shared[index] : value);
                })));
            }
            sheets.Add(entry.Name + "\n" + string.Join('\n', rows));
        }
        return string.Join("\n\n", sheets) + "\n［XLSX: 各シート先頭2,000行・最大32シート、セルの保存済み値。数式の再計算・書式・図は読み取り対象外です］";
    }

    // Windows' built-in PDF renderer is accessed through Windows PowerShell's WinRT projection.
    // No Office install, PDF library or PowerShell module is required.
    private static async Task<List<ChatAttachment>> ReadPdfAsync(string path, CancellationToken token)
    {
        string temp = Path.Combine(Path.GetTempPath(), "ElunaPdf-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            string script = """
                $ErrorActionPreference='Stop'
                $ProgressPreference='SilentlyContinue'
                Add-Type -AssemblyName System.Runtime.WindowsRuntime
                [Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime] > $null
                [Windows.Data.Pdf.PdfDocument,Windows.Data.Pdf,ContentType=WindowsRuntime] > $null
                [Windows.Storage.Streams.InMemoryRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime] > $null
                [Windows.Storage.Streams.DataReader,Windows.Storage.Streams,ContentType=WindowsRuntime] > $null
                function Await($operation,$type) {
                    $method=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
                    $task=$method.MakeGenericMethod($type).Invoke($null,@($operation)); $task.Wait(); $task.Result
                }
                $file=Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($env:ELUNA_PDF_INPUT)) ([Windows.Storage.StorageFile])
                $pdf=Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
                if ($pdf.PageCount -gt 6) { throw 'PDFは6ページまでです。対象ページを分けて添付してください。' }
                for($i=0;$i -lt $pdf.PageCount;$i++) {
                    $page=$pdf.GetPage($i); $stream=New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
                    $options=New-Object Windows.Data.Pdf.PdfPageRenderOptions; $options.DestinationWidth=1280
                    $render=$page.RenderToStreamAsync($stream,$options)
                    $actionMethod=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and -not $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' } | Select-Object -First 1
                    $actionTask=$actionMethod.Invoke($null,@($render)); $actionTask.Wait()
                    $stream.Seek(0); $reader=[Windows.Storage.Streams.DataReader]::new($stream.GetInputStreamAt(0))
                    $loaded=Await ($reader.LoadAsync([uint32]$stream.Size)) ([uint32])
                    $bytes=New-Object byte[] ([int]$stream.Size); $reader.ReadBytes($bytes)
                    [IO.File]::WriteAllBytes((Join-Path $env:ELUNA_PDF_OUTPUT ($i.ToString()+'.png')),$bytes)
                    $reader.Dispose(); $stream.Dispose(); $page.Dispose()
                }
                """;
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell/v1.0/powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            script = "try {\n" + script + "\n} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
            foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) { start.ArgumentList.Add(arg); }
            start.Environment["ELUNA_PDF_INPUT"] = Path.GetFullPath(path); start.Environment["ELUNA_PDF_OUTPUT"] = temp;
            using var process = Process.Start(start) ?? throw new IOException("WindowsのPDF描画を開始できませんでした。");
            using var job = new WindowsJob(process); Task<string> error = process.StandardError.ReadToEndAsync(token); Task<string> stdout = process.StandardOutput.ReadToEndAsync(token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch { if (!process.HasExited) { process.Kill(true); } throw; }
            string detail = await error; await stdout;
            if (process.ExitCode != 0) { throw new InvalidDataException("PDFを読み取れませんでした。暗号化・6ページ超過・WindowsのPDF機能の利用可否を確認してください。\n" + detail[..Math.Min(detail.Length, 500)]); }
            return await Task.Run(() => Directory.GetFiles(temp, "*.png").OrderBy(p => int.Parse(Path.GetFileNameWithoutExtension(p))).Select((p, i) => Image(p, Path.GetFileName(path) + $" · {i + 1}ページ")).ToList(), token);
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
    }
}
