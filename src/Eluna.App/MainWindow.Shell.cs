using System.Windows;
using System.Windows.Controls;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App;

public partial class MainWindow
{
    public void OpenSettings(int tab = 0) { var settings = ShowUnique(() => new SettingsWindow(_viewModel) { Owner = this }); settings.SelectTab(tab); }
    private bool _showMembers = true;
    private void UpdateShellLayout()
    {
        bool show = _showMembers && RootLayout.ActualWidth >= 1120;
        ChannelTitleText.MaxWidth = RootLayout.ActualWidth < 1000 ? 140 : 250;
        ChannelDescriptionText.Visibility = RootLayout.ActualWidth < 1000 ? Visibility.Collapsed : Visibility.Visible;
        MembersColumn.Width = new(show ? 224 : 0); MembersPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (RootLayout.ActualWidth >= 1120) { MembersPopup.IsOpen = false; }
    }
    private void Members_Click(object sender, RoutedEventArgs e)
    {
        if (RootLayout.ActualWidth < 1120) { MembersPopup.IsOpen = !MembersPopup.IsOpen; }
        else { _showMembers = !_showMembers; UpdateShellLayout(); }
    }
    private void InsertMessageText(string text)
    { InputBox.Focus(); int caret = InputBox.SelectionStart; InputBox.SelectedText = text; InputBox.CaretIndex = caret + text.Length; }
    private static string Mention(AgentAccount account) => MentionService.Format(account);
    private void Mention_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) { return; }
        var menu = new ContextMenu { PlacementTarget = button };
        foreach (AgentAccount member in _viewModel.RoomMembers.Where(a => a.Enabled))
        { var item = new MenuItem { Header = member.Label }; item.Click += (_, _) => InsertMessageText(Mention(member)); menu.Items.Add(item); }
        if (_viewModel.RoomMembers.Count > 1) { var all = new MenuItem { Header = "@全員" }; all.Click += (_, _) => InsertMessageText("@全員 "); menu.Items.Add(all); }
        button.ContextMenu = menu; menu.IsOpen = true;
    }
    private void Emoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) { return; }
        var menu = new ContextMenu { PlacementTarget = button };
        foreach (string emoji in new[] { "😀", "👍", "🎉", "❤️", "🤔", "🙏", "✅", "👀" })
        { var item = new MenuItem { Header = emoji, FontSize = 22 }; item.Click += (_, _) => InsertMessageText(emoji); menu.Items.Add(item); }
        button.ContextMenu = menu; menu.IsOpen = true;
    }
    private void Member_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AgentAccount member }) { OpenProfile(member.Id); }
    }
    private void Speaker_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { DataContext: ChatMessage message }) { if (message.Role == "user" && message.Identity?.AgentId is null) { SelfProfile_Click(sender, e); } else if (message.Identity?.AgentId is Guid id) { OpenProfile(id); } } }
    internal void OpenProfile(Guid id)
    {
        if (id == AgentAccount.LocalId) { SelfProfile_Click(this, new RoutedEventArgs()); return; }
        MembersPopup.IsOpen = false;
        var existing = OwnedWindows.OfType<AgentProfileWindow>().FirstOrDefault(w => w.Tag is Guid current && current == id);
        if (existing is not null) { existing.Activate(); return; }
        new AgentProfileWindow(_viewModel, id) { Owner = this, Tag = id }.Show();
    }
    private void SelfProfile_Click(object sender, RoutedEventArgs e) => ShowUnique(() => new SelfProfileWindow(_viewModel) { Owner = this });
    private void InviteMembers_Click(object sender, RoutedEventArgs e)
    { if (_viewModel.SelectedConversation is { } room) { OpenInvitations(room); } }
    private void OpenInvitations(Conversation room)
    { new InviteMembersWindow(_viewModel, room) { Owner = this }.Show(); }
    private void CreateAccount_Click(object sender, RoutedEventArgs e) { var window = new AgentsWindow(_viewModel) { Owner = this }; window.BeginCreate(); window.Show(); }
    private void MentionLink_Clicked(object? sender, MentionEventArgs e) { if (e.Account is Guid id) { OpenProfile(id); } else if (e.All) { MembersPopup.IsOpen = true; } }
    internal void OpenMediaTools(string tab = "document", string? initial = null) => OpenMedia(tab, initial);
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
