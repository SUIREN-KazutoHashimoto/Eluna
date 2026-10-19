using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.Controls;

public sealed class ReferenceEventArgs(string kind, int number) : EventArgs { public string Kind { get; } = kind; public int Number { get; } = number; }
public sealed class MentionEventArgs(Guid? account, bool all) : EventArgs { public Guid? Account { get; } = account; public bool All { get; } = all; }

public sealed class MarkdownView : UserControl
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(nameof(Markdown), typeof(string), typeof(MarkdownView), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty TextSizeProperty = DependencyProperty.Register(nameof(TextSize), typeof(double), typeof(MarkdownView), new PropertyMetadata(15.0, Changed));
    public static readonly DependencyProperty TextLineHeightProperty = DependencyProperty.Register(nameof(TextLineHeight), typeof(double), typeof(MarkdownView), new PropertyMetadata(25.0, Changed));
    public static readonly DependencyProperty PlainTextProperty = DependencyProperty.Register(nameof(PlainText), typeof(bool), typeof(MarkdownView), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty AccountsProperty = DependencyProperty.Register(nameof(Accounts), typeof(IEnumerable<AgentAccount>), typeof(MarkdownView), new PropertyMetadata(null, AccountsChanged));
    public bool PlainText { get => (bool)GetValue(PlainTextProperty); set => SetValue(PlainTextProperty, value); }
    public IEnumerable<AgentAccount>? Accounts { get => (IEnumerable<AgentAccount>?)GetValue(AccountsProperty); set => SetValue(AccountsProperty, value); }
    public string Markdown { get => (string)GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }
    public double TextSize { get => (double)GetValue(TextSizeProperty); set => SetValue(TextSizeProperty, value); }
    public double TextLineHeight { get => (double)GetValue(TextLineHeightProperty); set => SetValue(TextLineHeightProperty, value); }
    private readonly RichTextBox _reader;
    private readonly DispatcherTimer _renderTimer;
    private readonly Button _resume;
    private readonly List<MarkdownBlock> _rendered = [];
    private bool _dirty = true;
    private double _font, _line;
    public event EventHandler<ReferenceEventArgs>? ReferenceClicked;
    public event EventHandler<MentionEventArgs>? MentionClicked;

    public MarkdownView()
    {
        _reader = new RichTextBox
        {
            IsReadOnly = true,
            IsUndoEnabled = false,
            IsDocumentEnabled = true,
            BorderThickness = new(0),
            Padding = new(0),
            Background = Brushes.Transparent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsReadOnlyCaretVisible = true
        };
        _reader.SetResourceReference(ForegroundProperty, "TextBrush");
        var layout = new DockPanel();
        _resume = new Button { Content = "選択中の更新を表示（選択を解除）", Visibility = Visibility.Collapsed, Padding = new(8, 3, 8, 3), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left };
        DockPanel.SetDock(_resume, Dock.Top); layout.Children.Add(_resume); var body = new Grid(); body.Children.Add(_reader); body.Children.Add(new RichEmojiLayer(_reader)); layout.Children.Add(body); Content = layout;
        _resume.Click += (_, _) => { _reader.Selection.Select(_reader.Document.ContentEnd, _reader.Document.ContentEnd); foreach (CodeView code in Codes()) { code.ClearSelection(); } Render(); };
        _reader.SelectionChanged += (_, _) => { if (_dirty && _reader.Selection.IsEmpty) { _renderTimer?.Start(); } };
        _renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => { _renderTimer!.Stop(); Render(); }, Dispatcher);
        _renderTimer.Stop();
        Loaded += (_, _) => { ObserveAccounts(true); if (_parseCancellation.IsCancellationRequested) { _parseCancellation.Dispose(); _parseCancellation = new(); } Render(); };
        DataContextChanged += (_, _) => { _parser = new(); _rendered.Clear(); _reader.Document = new FlowDocument(); _dirty = true; _renderTimer.Start(); };
        Unloaded += (_, _) => { ObserveAccounts(false); _renderTimer.Stop(); _parseCancellation.Cancel(); };
    }

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs _)
    { var view = (MarkdownView)sender; view._dirty = true; if (_.Property == PlainTextProperty) { view._rendered.Clear(); } view._renderTimer?.Start(); }
    private static void AccountsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MarkdownView)d;
        if (e.OldValue is INotifyCollectionChanged old) { old.CollectionChanged -= view.MembersChanged; }
        view.ObserveAccounts(view.IsLoaded);
        view.MembersChanged(null, null!);
    }
    private void ObserveAccounts(bool observe) { if (Accounts is INotifyCollectionChanged accounts) { accounts.CollectionChanged -= MembersChanged; if (observe) { accounts.CollectionChanged += MembersChanged; } } }
    private void MembersChanged(object? sender, NotifyCollectionChangedEventArgs e) { _rendered.Clear(); _dirty = true; _renderTimer?.Start(); }
    private IEnumerable<CodeView> Codes() => _reader.Document.Blocks.OfType<BlockUIContainer>().Select(b => b.Child).OfType<CodeView>();
    private static bool Same(MarkdownBlock a, MarkdownBlock b) => a.Kind == b.Kind && a.Text == b.Text && a.Level == b.Level && a.Language == b.Language &&
        (a.Rows is null && b.Rows is null || a.Rows is not null && b.Rows is not null && a.Rows.Count == b.Rows.Count && a.Rows.Zip(b.Rows).All(pair => pair.First.SequenceEqual(pair.Second)));

    private StreamingMarkdownParser _parser = new();
    private bool _parsing;
    private CancellationTokenSource _parseCancellation = new();
    private async void Render()
    {
        if (!_dirty) { return; }
        if (!_reader.Selection.IsEmpty || Codes().Any(code => code.HasSelection)) { _resume.Visibility = Visibility.Visible; return; }
        _resume.Visibility = Visibility.Collapsed;
        if (_parsing) { return; }
        _parsing = true; string markdown = Markdown; StreamingMarkdownParser parser = _parser;
        IReadOnlyList<MarkdownBlock> parsed;
        CancellationToken token = _parseCancellation.Token;
        try { parsed = PlainText ? [new MarkdownBlock("plain", markdown)] : await Task.Run(() => parser.Update(markdown, token), token); }
        catch (OperationCanceledException) { return; }
        finally { _parsing = false; if (_dirty && IsLoaded) { _renderTimer.Start(); } }
        if (!IsLoaded || parser != _parser || markdown != Markdown) { if (IsLoaded) { _renderTimer.Start(); } return; }
        if (!_reader.Selection.IsEmpty || Codes().Any(code => code.HasSelection)) { _resume.Visibility = Visibility.Visible; return; }
        int common = 0;
        if (_font == TextSize && _line == TextLineHeight) { while (common < _rendered.Count && common < parsed.Count && Same(_rendered[common], parsed[common])) { common++; } }
        var codeStates = _reader.Document.Blocks.Select((block, index) => (block, index)).Where(item => item.block is BlockUIContainer { Child: CodeView }).ToDictionary(item => item.index, item => ((CodeView)((BlockUIContainer)item.block).Child).State);
        FlowDocument document = _reader.Document; document.PagePadding = new(0); document.FontFamily = FontFamily; document.FontSize = TextSize; document.LineHeight = TextLineHeight;
        document.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        while (document.Blocks.Count > common) { document.Blocks.Remove(document.Blocks.LastBlock); }
        _font = TextSize; _line = TextLineHeight;
        foreach (MarkdownBlock block in parsed.Skip(common))
        {
            if (block.Kind == "code")
            {
                var code = new CodeView(block.Text, block.Language, TextSize, codeStates.GetValueOrDefault(document.Blocks.Count));
                code.SelectionCleared += (_, _) => { if (_dirty) { _renderTimer.Start(); } };
                document.Blocks.Add(new BlockUIContainer(code) { Margin = new(0, 4, 0, 10) }); continue;
            }
            if (block.Kind == "rule") { var rule = new Border { Height = 1, Margin = new(0, 8, 0, 12) }; rule.SetResourceReference(Border.BackgroundProperty, "StrokeBrush"); document.Blocks.Add(new BlockUIContainer(rule)); continue; }
            if (block.Kind == "table" && block.Rows is { Count: > 0 } rows)
            {
                var table = new Table { CellSpacing = 0, Margin = new(0, 4, 0, 10) };
                int width = rows.Max(r => r.Length);
                for (int i = 0; i < width; i++) { table.Columns.Add(new TableColumn()); }
                var group = new TableRowGroup();
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var row = new TableRow();
                    for (int col = 0; col < width; col++)
                    {
                        var cell = new TableCell(Paragraph(col < rows[rowIndex].Length ? rows[rowIndex][col] : ""))
                        {
                            Padding = new(8, 5, 8, 5),
                            BorderThickness = new(0.5),
                            FontWeight = rowIndex == 0 ? FontWeights.SemiBold : FontWeights.Normal
                        };
                        cell.SetResourceReference(TableCell.BorderBrushProperty, "StrokeBrush");
                        if (rowIndex == 0) { cell.SetResourceReference(TextElement.BackgroundProperty, "SurfaceBrush"); }
                        row.Cells.Add(cell);
                    }
                    group.Rows.Add(row);
                }
                table.RowGroups.Add(group);
                document.Blocks.Add(table);
                continue;
            }
            Paragraph paragraph = Paragraph(block.Kind == "list" ? block.Language + "  " + block.Text : block.Text);
            if (block.Kind == "heading") { paragraph.FontWeight = FontWeights.SemiBold; paragraph.FontSize = TextSize + Math.Max(2, 9 - block.Level * 2); paragraph.Margin = new(0, 7, 0, 7); }
            if (block.Kind == "quote") { paragraph.BorderThickness = new(3, 0, 0, 0); paragraph.SetResourceReference(System.Windows.Documents.Paragraph.BorderBrushProperty, "StrokeBrush"); paragraph.Padding = new(14, 4, 0, 4); paragraph.SetResourceReference(TextElement.ForegroundProperty, "MutedBrush"); }
            if (block.Kind == "list") { paragraph.Margin = new(4, 0, 0, 7); }
            document.Blocks.Add(paragraph);
        }
        _rendered.Clear(); _rendered.AddRange(parsed); _dirty = false;
    }

    private Paragraph Paragraph(string text)
    {
        var paragraph = new Paragraph { Margin = PlainText ? new(0) : new(0, 0, 0, 8) };
        if (PlainText) { AddText(paragraph.Inlines, text); } else { AddInlines(paragraph.Inlines, text); }
        return paragraph;
    }

    private void AddInlines(InlineCollection inlines, string text, int depth = 0)
    {
        if (depth > 8) { AddText(inlines, text); return; }
        string pattern = @"(?<escaped>\\(?<escapeChar>[\\`*_{}\[\]()#+\-.!>~]))|(?<reference>\[(?<referenceKind>[SK])(?<referenceNumber>\d{1,3})\](?!\())|(?<link>\[(?<label>[^\]\n]+)\]\((?:<(?<url>https?://[^\s>]+)>|(?<url>https?://[^\s)]+))\))|(?<code>`(?<codeText>[^`]+)`)|(?<strongEm>\*\*\*(?<strongEmText>.+?)\*\*\*)|(?<bold>\*\*(?<boldText>.+?)\*\*)|(?<strike>~~(?<strikeText>.+?)~~)|(?<italic>(?<!\*)\*(?<italicText>[^*\n]+)\*(?!\*))";
        int start = 0;
        Match[] matches;
        try { matches = Regex.Matches(text, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)).ToArray(); }
        catch (RegexMatchTimeoutException) { AddText(inlines, text); return; }
        foreach (Match match in matches)
        {
            if (match.Index > start) { AddText(inlines, text[start..match.Index]); }
            if (match.Groups["escaped"].Success) { inlines.Add(new Run(match.Groups["escapeChar"].Value)); }
            else if (match.Groups["reference"].Success)
            {
                var link = new Hyperlink(new Run(match.Value)) { ToolTip = "生成時の根拠を開く" }; link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                link.Click += (_, e) => { ReferenceClicked?.Invoke(this, new(match.Groups["referenceKind"].Value, int.Parse(match.Groups["referenceNumber"].Value, System.Globalization.CultureInfo.InvariantCulture))); e.Handled = true; }; inlines.Add(link);
            }
            else if (match.Groups["link"].Success && Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out Uri? uri))
            {
                var link = new Hyperlink(new Run(match.Groups["label"].Value)) { NavigateUri = uri, ToolTip = uri.AbsoluteUri };
                link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                link.RequestNavigate += (_, e) =>
                {
                    if (e.Uri.Scheme is "http" or "https")
                    {
                        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { MessageBox.Show("リンクを開けませんでした。", "Eluna"); }
                    }
                    e.Handled = true;
                };
                inlines.Add(link);
            }
            else if (match.Groups["code"].Success)
            {
                var run = new Run(match.Groups["codeText"].Value) { FontFamily = new("Cascadia Mono, Consolas"), FontSize = Math.Max(11, TextSize - 1) };
                run.SetResourceReference(TextElement.BackgroundProperty, "SurfaceBrush");
                inlines.Add(run);
            }
            else if (match.Groups["strongEm"].Success) { var bold = new Bold(); var italic = new Italic(); AddInlines(italic.Inlines, match.Groups["strongEmText"].Value, depth + 1); bold.Inlines.Add(italic); inlines.Add(bold); }
            else if (match.Groups["bold"].Success) { var bold = new Bold(); AddInlines(bold.Inlines, match.Groups["boldText"].Value, depth + 1); inlines.Add(bold); }
            else if (match.Groups["strike"].Success) { var strike = new Span { TextDecorations = TextDecorations.Strikethrough }; AddInlines(strike.Inlines, match.Groups["strikeText"].Value, depth + 1); inlines.Add(strike); }
            else { var italic = new Italic(); AddInlines(italic.Inlines, match.Groups["italicText"].Value, depth + 1); inlines.Add(italic); }
            start = match.Index + match.Length;
        }
        if (start < text.Length) { AddText(inlines, text[start..]); }
    }

    private void AddText(InlineCollection inlines, string text)
    {
        void Plain(string part) { string[] lines = part.Split('\n'); for (int i = 0; i < lines.Length; i++) { if (i > 0) { inlines.Add(new LineBreak()); } int at = 0; foreach (var element in EmojiRasterizer.Elements(lines[i]).Where(e => e.Emoji)) { if (element.Index > at) { inlines.Add(new Run(lines[i][at..element.Index])); } inlines.Add(new EmojiRun(element.Text) { FontFamily = new FontFamily("Segoe UI Emoji") }); at = element.Index + element.Text.Length; } if (at < lines[i].Length) { inlines.Add(new Run(lines[i][at..])); } } }
        int at = 0;
        foreach (MentionToken token in MentionService.Scan(text, Accounts ?? []))
        {
            Plain(text[at..token.Start]);
            var link = new Hyperlink(new Run(token.Text)) { TextDecorations = null, FontWeight = FontWeights.SemiBold, ToolTip = token.All ? "メンバー一覧" : "プロフィールを開く" };
            link.SetResourceReference(TextElement.ForegroundProperty, "MentionTextBrush"); link.SetResourceReference(TextElement.BackgroundProperty, "MentionBackgroundBrush");
            link.Click += (_, e) => { MentionClicked?.Invoke(this, new(token.Accounts.Count == 1 ? token.Accounts[0] : null, token.All)); e.Handled = true; }; inlines.Add(link); at = token.Start + token.Length;
        }
        Plain(text[at..]);
    }
}

internal sealed record CodeViewState(bool Wrap, double Offset);

internal sealed class CodeView : Border
{
    public event EventHandler? SelectionCleared;
    private readonly RichTextBox _reader;
    private readonly CheckBox _wrap;
    public bool HasSelection => !_reader.Selection.IsEmpty;
    public CodeViewState State => new(_wrap.IsChecked == true, _reader.HorizontalOffset);
    public void ClearSelection() => _reader.Selection.Select(_reader.Document.ContentEnd, _reader.Document.ContentEnd);
    public CodeView(string code, string language, double fontSize, CodeViewState? state = null)
    {
        SetResourceReference(BackgroundProperty, "SidebarBrush");
        CornerRadius = new(12);
        Padding = new(10);
        var panel = new Grid();
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new(0, 0, 0, 8) };
        var copy = new Button { Content = "コピー", Padding = new(9, 4, 9, 4), FontSize = 12 };
        DockPanel.SetDock(copy, Dock.Right);
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(code); copy.Content = "コピーしました"; }
            catch (System.Runtime.InteropServices.COMException) { copy.Content = "再試行"; }
        };
        var wrap = _wrap = new CheckBox { Content = "折り返す", IsChecked = state?.Wrap ?? true, FontSize = 12, Margin = new(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        wrap.SetResourceReference(Control.ForegroundProperty, "MutedBrush");
        DockPanel.SetDock(wrap, Dock.Right);
        header.Children.Add(copy);
        header.Children.Add(wrap);
        var label = new TextBlock { Text = string.IsNullOrEmpty(language) ? "コード" : language, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        header.Children.Add(label);
        panel.Children.Add(header);
        var document = new FlowDocument { PagePadding = new(0), FontFamily = new("Cascadia Mono, Consolas"), FontSize = Math.Max(11, fontSize - 1) };
        var paragraph = new Paragraph { Margin = new(0) };
        foreach (CodeToken token in MarkdownParser.Highlight(code, language))
        {
            var run = new Run(token.Text);
            if (token.Kind == "plain") { run.SetResourceReference(TextElement.ForegroundProperty, "TextBrush"); }
            else { run.SetResourceReference(TextElement.ForegroundProperty, token.Kind switch { "keyword" => "CodeKeywordBrush", "string" => "CodeStringBrush", "comment" => "CodeCommentBrush", _ => "CodeNumberBrush" }); }
            paragraph.Inlines.Add(run);
        }
        document.Blocks.Add(paragraph);
        var reader = _reader = new RichTextBox
        {
            Document = document,
            IsReadOnly = true,
            IsUndoEnabled = false,
            BorderThickness = new(0),
            Padding = new(0),
            Background = Brushes.Transparent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        reader.SelectionChanged += (_, _) => { if (reader.Selection.IsEmpty) { SelectionCleared?.Invoke(this, EventArgs.Empty); } };
        wrap.Checked += (_, _) => document.ClearValue(FlowDocument.PageWidthProperty);
        wrap.Unchecked += (_, _) => document.PageWidth = Math.Min(30000, Math.Max(800, code.Split('\n').Max(l => l.Length) * fontSize));
        if (state?.Wrap == false) { document.PageWidth = Math.Min(30000, Math.Max(800, code.Split('\n').Max(l => l.Length) * fontSize)); }
        Loaded += (_, _) => { if (state is not null) { reader.ScrollToHorizontalOffset(state.Offset); } };
        Grid.SetRow(reader, 1);
        panel.Children.Add(reader);
        Child = panel;
    }
}
