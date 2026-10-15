using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Services;

namespace Eluna.App;

public sealed class SkillDiffWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    public SkillDiffWindow(IReadOnlyDictionary<string, byte[]> before, IReadOnlyDictionary<string, byte[]> after)
    {
        Title = "スキルの変更差分 · Eluna"; Width = 1060; Height = 760; MinWidth = 720; MinHeight = 480; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(20) }; Content = root;
        var heading = new TextBlock { Text = "保存した版と編集中の内容を比較", FontSize = 21, Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) }; DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new(240) }); grid.ColumnDefinitions.Add(new()); root.Children.Add(grid);
        string[] paths = before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(p => !p.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        var files = new ListBox { Margin = new(0, 0, 12, 0) }; System.Windows.Automation.AutomationProperties.SetName(files, "変更を比較するファイル");
        foreach (string path in paths)
        {
            string kind = !before.ContainsKey(path) ? "追加" : !after.ContainsKey(path) ? "削除" : before[path].AsSpan().SequenceEqual(after[path]) ? "変更なし" : "変更";
            files.Items.Add(new ListBoxItem { Content = kind + " · " + path, Tag = path, ToolTip = path });
        }
        grid.Children.Add(files);
        var tabs = new TabControl(); Grid.SetColumn(tabs, 1); grid.Children.Add(tabs);
        TextBox Viewer(string name) { var box = new TextBox { IsReadOnly = true, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new("Consolas, Yu Gothic UI") }; System.Windows.Automation.AutomationProperties.SetName(box, name); return box; }
        var diff = Viewer("追加と削除の差分"); var oldText = Viewer("保存した版の全文"); var newText = Viewer("編集中の全文");
        foreach (var (label, content) in new[] { ("差分", diff), ("保存した版", oldText), ("編集中", newText) }) { tabs.Items.Add(new TabItem { Header = label, Content = content, Style = (Style)FindResource("WorkOptionsTab") }); }
        int revision = 0;
        files.SelectionChanged += async (_, _) =>
        {
            if (files.SelectedItem is not ListBoxItem { Tag: string path }) { return; }
            int current = ++revision; status.Text = "比較しています…";
            try
            {
                byte[] a = before.GetValueOrDefault(path) ?? [], b = after.GetValueOrDefault(path) ?? [];
                var comparison = await Task.Run(() =>
                {
                    string Preview(byte[] bytes) => bytes.Length > 512 * 1024 ? throw new DecoderFallbackException() : SkillFormat.Utf8.GetString(bytes) is string value && !value.Contains('\0') ? value : throw new DecoderFallbackException();
                    try { string left = Preview(a), right = Preview(b); var lines = TextDiff.Compare(left, right, _lifetime.Token); return (left, right, text: string.Join('\n', lines.Select(l => l.Kind + " " + l.Text)), summary: $"追加 {lines.Count(l => l.Kind == '+')}行 · 削除 {lines.Count(l => l.Kind == '-')}行"); }
                    catch (DecoderFallbackException) { return (left: $"{a.Length:N0} bytes\nSHA-256: {Convert.ToHexString(SHA256.HashData(a))}", right: $"{b.Length:N0} bytes\nSHA-256: {Convert.ToHexString(SHA256.HashData(b))}", text: "バイナリまたは512KB超のファイルです。容量とSHA-256で変更を確認できます。", summary: "全文の差分表示は省略しました。"); }
                }, _lifetime.Token);
                if (current != revision || _lifetime.IsCancellationRequested) { return; }
                oldText.Text = comparison.left; newText.Text = comparison.right; diff.Text = comparison.text; status.Text = comparison.summary;
            }
            catch (OperationCanceledException) { }
        };
        Closed += (_, _) => _lifetime.Cancel(); files.SelectedIndex = paths.Length > 0 ? 0 : -1;
    }
}
