using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class SkillPickerWindow : Window
{
    public SkillPickerWindow(ChatViewModel chat)
    {
        Title = "この会話のスキル · Eluna"; Width = 620; Height = 570; MinWidth = 480; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        Conversation? target = chat.SelectedConversation; Guid? conversationId = target?.Id; Guid? projectId = target?.ProjectId;
        var root = new DockPanel { Margin = new(24) };
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "この会話のスキル", FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "自動では依頼に合うスキルを選びます。指定したスキルは必ず手順を読み込みます（3件まで）。/スキル名 でも指定できます。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 12) });
        header.Children.Add(new TextBlock { Text = "対象: " + (target?.Title ?? "会話なし") + " · " + chat.ProjectContextLabel, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 0, 0, 8) });
        var back = new Button { Content = "対象の会話へ戻る", HorizontalAlignment = HorizontalAlignment.Left }; back.Click += (_, _) => { if (target is not null && chat.Conversations.Contains(target)) { chat.SelectedConversation = target; } }; header.Children.Add(back);
        var modes = new ComboBox { DisplayMemberPath = "Value", SelectedValuePath = "Key", ItemsSource = new[] { new KeyValuePair<string, string>("auto", "自動で選ぶ＋指定したスキル"), new("manual", "指定したスキルのみ"), new("off", "スキルを使わない") }, SelectedValue = chat.SkillMode, Margin = new(0, 0, 0, 16) }; System.Windows.Automation.AutomationProperties.SetName(modes, "この会話のスキル適用モード"); header.Children.Add(modes);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var manage = new Button { Content = "管理・取り込み", Margin = new(0, 0, 10, 0) };
        manage.Click += (_, _) => { if (target is not null && chat.Conversations.Contains(target)) { chat.SelectedConversation = target; } new SkillsWindow(chat) { Owner = Owner ?? this }.Show(); Close(); }; footer.Children.Add(manage);
        var checks = new List<(SkillRegistration Skill, CheckBox Box)>();
        var list = new StackPanel();
        foreach (SkillRegistration skill in chat.AvailableSkills)
        {
            var checkbox = new CheckBox { Content = skill.Name + " · " + skill.Scope, IsChecked = chat.SelectedConversation?.SkillIds.Contains(skill.Id) == true, Margin = new(0, 12, 0, 6) };
            list.Children.Add(checkbox); list.Children.Add(new TextBlock { Text = skill.Description, TextWrapping = TextWrapping.Wrap, Margin = new(22, 0, 0, 8), FontSize = 12 }); checks.Add((skill, checkbox));
        }
        if (checks.Count == 0) { list.Children.Add(new TextBlock { Text = "この範囲で有効なスキルはありません。「管理・取り込み」で追加できます。", TextWrapping = TextWrapping.Wrap }); }
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var save = new Button { Content = "適用" }; save.SetResourceReference(StyleProperty, "PrimaryButton");
        save.Click += async (_, _) =>
        {
            try
            {
                if (conversationId is not Guid id) { throw new InvalidOperationException("対象の会話がありません。"); }
                await chat.SetConversationSkillsAsync(id, projectId, checks.Where(p => p.Box.IsChecked == true).Select(p => p.Skill.Id), modes.SelectedValue as string ?? "auto"); Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "スキルを適用できませんでした"); }
        };
        footer.Children.Add(save); Content = root;
    }
}
