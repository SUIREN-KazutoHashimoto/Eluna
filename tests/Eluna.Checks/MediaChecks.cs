using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class MediaChecks
{
    internal static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        static void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
        string path = Path.Combine(root, "日本語.docx");
        const string markdown = "# 日本語の企画書\n\n本文です。Project ORBIT-4826\n\n| 項目 | 値 |\n| --- | --- |\n| 件数 | 27 |";
        await DocumentService.SaveTextAsync(path, markdown, default);
        List<ChatAttachment> docs = await AttachmentService.ReadAsync(path, default);
        Check(docs.Single().Text.Contains("日本語の企画書", StringComparison.Ordinal) && docs[0].Text.Contains("ORBIT-4826", StringComparison.Ordinal) && docs[0].Text.Contains("27", StringComparison.Ordinal), "DOCX generation/read lost headings, Japanese or table content.");
        string xlsx = Path.Combine(root, "table.xlsx");
        using (var zip = ZipFile.Open(xlsx, ZipArchiveMode.Create))
        {
            void Entry(string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(text); }
            Entry("xl/sharedStrings.xml", "<sst><si><r><t>日本語</t></r><r><t>の値</t></r></si></sst>");
            Entry("xl/worksheets/sheet1.xml", "<worksheet><sheetData><row><c r='A1' t='s'><v>0</v></c><c r='C1'><v>27</v></c><c r='D1' t='inlineStr'><is><t>inline</t></is></c></row></sheetData></worksheet>");
        }
        string table = (await AttachmentService.ReadAsync(xlsx, default))[0].Text;
        Check(table.Contains("A1=日本語の値", StringComparison.Ordinal) && table.Contains("C1=27", StringComparison.Ordinal) && table.Contains("D1=inline", StringComparison.Ordinal), "Spreadsheet coordinates or cached/inline/shared values were lost.");
        string pptx = Path.Combine(root, "slides.pptx");
        using (var zip = ZipFile.Open(pptx, ZipArchiveMode.Create))
        { foreach (int i in new[] { 10, 2 }) { using var writer = new StreamWriter(zip.CreateEntry($"ppt/slides/slide{i}.xml").Open()); writer.Write($"<sld><t>Slide {i}</t></sld>"); } }
        string slides = (await AttachmentService.ReadAsync(pptx, default))[0].Text;
        Check(slides.IndexOf("Slide 2", StringComparison.Ordinal) < slides.IndexOf("Slide 10", StringComparison.Ordinal), "Slide reading used lexicographic ordering.");
        string evil = Path.Combine(root, "evil.docx");
        using (var zip = ZipFile.Open(evil, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()); writer.Write("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///C:/private'>]><document><p><t>&e;</t></p></document>"); }
        bool blocked = false; try { await AttachmentService.ReadAsync(evil, default); } catch (System.Xml.XmlException) { blocked = true; }
        Check(blocked, "Document XML enabled external entities.");
        var conversation = new Conversation(); conversation.DraftAttachments.Add(docs[0]); conversation.Messages.Add(new() { Role = "user", Content = "要約して", Attachments = docs });
        conversation.Fork(1, "branch");
        var store = new AppStore(Path.Combine(root, "state")); await store.SaveAsync(new(1, new(), [conversation.Save()]));
        Conversation restored = Conversation.Restore((await store.LoadAsync()).Conversations.Single());
        Check(restored.DraftAttachments.Count == 1 && restored.Messages[0].Attachments.Single().Text.Contains("4826", StringComparison.Ordinal) && restored.Branches.Count == 2, "Attachment draft, branches or persistence failed.");
        using JsonDocument payload = JsonSerializer.SerializeToDocument(LlamaService.CreatePayload([restored.Messages[0].Save()], InferenceModes.Find("fast"), new(0, 128)));
        Check(payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!.Contains("4826", StringComparison.Ordinal), "Document content did not enter model input.");
        bool invalidImage = false;
        try { AppStore.Validate(new(1, new(), [conversation.Save() with { DraftAttachments = [new("broken.jpg", "image", ImageBase64: "bad base64")] }])); } catch (InvalidDataException) { invalidImage = true; }
        Check(invalidImage, "Imported malformed image was accepted.");
        var longDocument = new ChatAttachment("long.txt", "document", new string('x', 12000) + "\nProject code: ORBIT-4826");
        string excerpt = longDocument.ForQuestion("What is the project code?", 1800);
        Check(excerpt.Contains("ORBIT-4826", StringComparison.Ordinal) && excerpt.Contains("抜粋", StringComparison.Ordinal) && excerpt.Length < 1800, "Long-document retrieval lost a relevant late section or hid its reading limit.");
        Console.WriteLine("Media: Japanese DOCX round-trip, tables/slides, XML isolation, attachment drafts/branches/model input and invalid image rejection: OK");
    }
}
