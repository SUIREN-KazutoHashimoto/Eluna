using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace Eluna.App.Services;

public static class DocumentService
{
    public static Task SaveTextAsync(string path, string markdown, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase)) { WriteDocx(path, markdown); }
        else { File.WriteAllText(path, markdown, new UTF8Encoding(false)); }
    }, token);

    internal static void WriteDocx(string path, string markdown)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XElement Paragraph(string text, int heading = 0) => new(w + "p", new XElement(w + "pPr", heading > 0 ? new XElement(w + "keepNext") : null, new XElement(w + "spacing", new XAttribute(w + "after", 160)), heading > 0 ? new XElement(w + "outlineLvl", new XAttribute(w + "val", Math.Clamp(heading - 1, 0, 8))) : null),
            new XElement(w + "r", new XElement(w + "rPr", new XElement(w + "rFonts", new XAttribute(w + "ascii", "Yu Gothic"), new XAttribute(w + "eastAsia", "游ゴシック")), heading > 0 ? new XElement(w + "b") : null, new XElement(w + "sz", new XAttribute(w + "val", heading > 0 ? Math.Max(26, 44 - heading * 4) : 22))),
                new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Plain(text))));
        var body = new XElement(w + "body");
        foreach (MarkdownBlock block in MarkdownParser.Parse(markdown))
        {
            if (block.Kind == "table" && block.Rows is not null)
            {
                body.Add(new XElement(w + "tbl", new XElement(w + "tblPr", new XElement(w + "tblW", new XAttribute(w + "w", 0), new XAttribute(w + "type", "auto")), new XElement(w + "tblBorders", new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(side => new XElement(w + side, new XAttribute(w + "val", "single"), new XAttribute(w + "sz", 4), new XAttribute(w + "color", "CCCCCC"))))),
                    new XElement(w + "tblGrid", Enumerable.Range(0, block.Rows.Max(row => row.Length)).Select(_ => new XElement(w + "gridCol", new XAttribute(w + "w", 2200)))),
                    block.Rows.Select(row => new XElement(w + "tr", row.Select(cell => new XElement(w + "tc", Paragraph(cell)))))));
            }
            else { foreach (string line in block.Text.Split('\n')) { body.Add(Paragraph(line, block.Kind == "heading" ? block.Level : 0)); } }
        }
        body.Add(new XElement(w + "sectPr", new XElement(w + "pgSz", new XAttribute(w + "w", 11906), new XAttribute(w + "h", 16838)), new XElement(w + "pgMar", new[] { "top", "right", "bottom", "left" }.Select(side => new XAttribute(w + side, 1134)))));
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Entry(string name, string xml) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(xml); }
        Entry("[Content_Types].xml", "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='xml' ContentType='application/xml'/><Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/></Types>");
        Entry("_rels/.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/></Relationships>");
        Entry("word/document.xml", new XDocument(new XElement(w + "document", body)).ToString());
    }
    internal static FlowDocument Flow(string markdown)
    {
        var document = new FlowDocument { FontFamily = new("Yu Gothic"), FontSize = 15, LineHeight = 25, PageWidth = 794, PageHeight = 1123, PagePadding = new(60), ColumnWidth = double.PositiveInfinity, Foreground = Brushes.Black, Background = Brushes.White };
        foreach (MarkdownBlock block in MarkdownParser.Parse(markdown))
        {
            string text = block.Kind == "table" && block.Rows is not null ? string.Join('\n', block.Rows.Select(row => string.Join("  |  ", row))) : block.Text;
            document.Blocks.Add(new Paragraph(new Run(Plain(text))) { FontSize = block.Kind == "heading" ? Math.Max(18, 32 - block.Level * 3) : 15, FontWeight = block.Kind == "heading" ? FontWeights.Bold : FontWeights.Normal, Margin = new(0, 0, 0, 12), KeepWithNext = block.Kind == "heading" });
        }
        return document;
    }
    // A portable image PDF: preserves Japanese appearance without embedding Windows font files.
    // Text is rasterized; DOCX/MD/TXT are available when selectable/editable text is needed.
    public static async Task SavePdfAsync(string path, string markdown, CancellationToken token)
    {
        FlowDocument document = Flow(markdown); DocumentPaginator paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.PageSize = new(794, 1123); paginator.ComputePageCount();
        if (paginator.PageCount > 60) { throw new InvalidDataException("PDF出力は60ページまでです。"); }
        var pages = new List<byte[]>();
        for (int i = 0; i < paginator.PageCount; i++)
        {
            token.ThrowIfCancellationRequested(); DocumentPage page = paginator.GetPage(i);
            var target = new RenderTargetBitmap(1191, 1685, 144, 144, PixelFormats.Pbgra32); target.Render(page.Visual);
            using var bytes = new MemoryStream(); var encoder = new JpegBitmapEncoder { QualityLevel = 92 }; encoder.Frames.Add(BitmapFrame.Create(target)); encoder.Save(bytes); pages.Add(bytes.ToArray());
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        }
        await Task.Run(() => WritePdf(path, pages), token);
    }
    internal static void WritePdf(string path, IReadOnlyList<byte[]> pages)
    {
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write);
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));
        var offsets = new List<long> { 0 };
        void Object(int number, string content) { offsets.Add(output.Position); Write($"{number} 0 obj\n{content}\nendobj\n"); }
        Write("%PDF-1.4\n"); Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, $"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{3 + i * 3} 0 R"))}] >>");
        for (int i = 0; i < pages.Count; i++)
        {
            int n = 3 + i * 3;
            Object(n, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im0 {n + 1} 0 R >> >> /Contents {n + 2} 0 R >>");
            offsets.Add(output.Position); Write($"{n + 1} 0 obj\n<< /Type /XObject /Subtype /Image /Width 1191 /Height 1685 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {pages[i].Length} >>\nstream\n"); output.Write(pages[i]); Write("\nendstream\nendobj\n");
            const string drawing = "q 595 0 0 842 0 0 cm /Im0 Do Q\n";
            Object(n + 2, $"<< /Length {drawing.Length} >>\nstream\n{drawing}endstream");
        }
        long xref = output.Position; Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (long offset in offsets.Skip(1)) { Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"); }
        Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
    private static string Plain(string text) => Regex.Replace(Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", "$1 ($2)"), @"\*\*([^*]+)\*\*|`([^`]+)`", "$1$2");
}
