using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class TeamWindow : Window
{
    public TeamWindow(ChatViewModel chat, ChatMessage answer)
    {
        Title = "チームの作業 · Eluna"; Width = 580; Height = 560; MinWidth = 380; MinHeight = 350; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(22) }; Content = root;
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 14) }; var list = new StackPanel(); var body = new StackPanel(); body.Children.Add(status); body.Children.Add(list); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var resume = new Button { Content = "中断した作業を再開", Margin = new(0, 0, 8, 0) }; actions.Children.Add(resume); resume.Click += async (_, _) => { try { await chat.ResumeTeamAsync(answer); } catch (Exception ex) { status.Text = ex.Message; } };
        var stop = new Button { Content = "停止" }; actions.Children.Add(stop); stop.Click += (_, _) => chat.StopAnswer(answer);
        void Refresh()
        {
            if (answer.Team is not { } team) { return; }
            status.Text = team.Label + "\n" + team.Detail + $"\n出力 {team.TokensUsed:N0} / 8,192 token · {team.SecondsUsed:F0} / 300秒";
            resume.IsEnabled = team.Stage == "paused" && !answer.IsGenerating; stop.IsEnabled = answer.IsGenerating;
            list.Children.Clear();
            foreach (TeamAssignment task in team.Assignments)
            {
                string room = chat.Conversations.FirstOrDefault(c => c.Id == task.RoomId)?.Title ?? "削除されたチャンネル", name = chat.Agents.FirstOrDefault(a => a.Id == task.AgentId)?.Name ?? "アカウント";
                var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 8), Content = new TextBlock { Text = (task.Complete ? "✓ " : "○ ") + "# " + room + " / " + name + "\n" + task.Task, TextWrapping = TextWrapping.Wrap } }; button.Click += (_, _) => chat.OpenTeamRoom(task); list.Children.Add(button);
            }
        }
        void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(ChatMessage.Team) or nameof(ChatMessage.IsGenerating)) { Refresh(); } }
        answer.PropertyChanged += Changed; Closed += (_, _) => answer.PropertyChanged -= Changed; Refresh();
    }
}
