using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Eluna.App.Models;
using Eluna.App.Services;
using Microsoft.Win32;

namespace Eluna.App;

public sealed class MediaResultWindow : Window
{
    public MediaResultWindow(MediaJob job, string dataDirectory)
    {
        Title = job.Title + " · Eluna"; Width = 900; Height = 720; MinWidth = 620; MinHeight = 440; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(20) }; Content = root;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(status, Dock.Top); root.Children.Add(status);
        var buttons = new WrapPanel { Margin = new(0, 10, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var save = new Button { Content = "コピーを保存…" }; buttons.Children.Add(save);
        var knowledge = new Button { Content = "生成時のナレッジ" }; knowledge.Click += (_, _) => new KnowledgeWindow(job.Knowledge) { Owner = this }.Show(); buttons.Children.Add(knowledge);
        var skills = new Button { Content = "使用スキル" }; skills.Click += (_, _) => new SkillUseWindow(job.Skills, dataDirectory) { Owner = this }.Show(); buttons.Children.Add(skills);
        bool image = job.Request?.Kind == "image";
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(text, "生成した本文");
        var preview = new Image { Stretch = Stretch.Uniform }; root.Children.Add(image ? preview : text);
        bool SafeOutput() => job.Request is { } request && Path.GetFullPath(request.OutputPath).StartsWith(Path.Combine(dataDirectory, "Generated") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        void Refresh()
        {
            status.Text = job.Detail + "\n" + (job.Request?.Context?.Model.DisplayName ?? "画像モデル") + " · " + (job.Request?.ProjectId is Guid id ? job.Request.Context?.State.Projects?.FirstOrDefault(p => p.Id == id)?.Name ?? "プロジェクト限定" : "通常チャット");
            if (text.SelectionLength == 0 && text.Text != job.Text) { text.Text = job.Text; }
            if (image && SafeOutput() && job.State == "complete" && preview.Source is null && File.Exists(job.Request!.OutputPath)) { preview.Source = MediaWindow.LoadImage(job.Request.OutputPath); }
            save.IsEnabled = image ? preview.Source is not null : job.Text.Length > 0;
        }
        save.Click += async (_, _) =>
        {
            var picker = new SaveFileDialog { Filter = image ? "PNG|*.png" : "Markdown|*.md|テキスト|*.txt|Word文書|*.docx", FileName = image ? "Eluna-image.png" : "Eluna-document.md" };
            if (picker.ShowDialog(this) != true) { return; }
            try { if (image && SafeOutput()) { File.Copy(job.Request!.OutputPath, picker.FileName, true); } else if (!image) { await DocumentService.SaveTextAsync(picker.FileName, job.Text, CancellationToken.None); } }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = DiagnosticsService.Explain(ex); }
        };
        text.SelectionChanged += (_, _) => { if (text.SelectionLength == 0 && text.Text != job.Text) { Refresh(); } };
        System.ComponentModel.PropertyChangedEventHandler changed = (_, _) => Refresh(); job.PropertyChanged += changed; Closed += (_, _) => job.PropertyChanged -= changed; Refresh();
    }
}
