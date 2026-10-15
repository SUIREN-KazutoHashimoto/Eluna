using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App;

public sealed class SkillUseWindow : Window
{
    public SkillUseWindow(IReadOnlyList<AppliedSkill> skills, string? dataDirectory = null)
    {
        Title = "使用したスキル · Eluna"; Width = 900; Height = 730; MinWidth = 600; MinHeight = 440; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(24) };
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "この回答で使ったスキル", FontSize = 24 });
        foreach (AppliedSkill skill in skills) { header.Children.Add(new TextBlock { Text = skill.Name + " · " + skill.Reason + " · " + skill.Revision[..Math.Min(12, skill.Revision.Length)], Margin = new(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap }); }
        var footer = new WrapPanel { Margin = new(0, 16, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (dataDirectory is not null)
        {
            string prefix = Path.GetFullPath(Path.Combine(dataDirectory, "Generated", "Skills")) + Path.DirectorySeparatorChar;
            foreach (string artifact in skills.SelectMany(s => s.Executions).SelectMany(e => e.Artifacts))
            {
                try
                {
                    string full = Path.GetFullPath(artifact);
                    if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(full) is string directory) { directories.Add(directory); }
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
            }
        }
        foreach (string directory in directories)
        {
            var button = new Button { Content = "作成ファイルのフォルダーを開く", Margin = new(0, 0, 8, 0) };
            button.Click += (_, _) => { try { if (Directory.Exists(directory)) { Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true }); } } catch (Exception ex) { MessageBox.Show(this, ex.Message); } }; footer.Children.Add(button);
        }
        var close = new Button { Content = "閉じる" }; close.Click += (_, _) => Close(); footer.Children.Add(close);
        root.Children.Add(new TextBox { Text = SkillRunner.Context(skills), IsReadOnly = true, AcceptsReturn = true, VerticalContentAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 16, 0, 0) }); Content = root;
    }
}
