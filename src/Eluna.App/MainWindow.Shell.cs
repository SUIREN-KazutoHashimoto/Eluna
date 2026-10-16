using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;

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
    private static string Mention(AgentAccount account) => account.Name.Any(char.IsWhiteSpace) ? "@{" + account.Name + "} " : "@" + account.Name + " ";
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
    { if (sender is FrameworkElement { DataContext: ChatMessage { Identity.AgentId: Guid id } }) { OpenProfile(id); } }
    internal void OpenProfile(Guid id)
    {
        MembersPopup.IsOpen = false;
        var existing = OwnedWindows.OfType<AgentProfileWindow>().FirstOrDefault(w => w.Tag is Guid current && current == id);
        if (existing is not null) { existing.Activate(); return; }
        new AgentProfileWindow(_viewModel, id) { Owner = this, Tag = id }.Show();
    }
    internal void OpenMediaTools(string tab = "document", string? initial = null) => OpenMedia(tab, initial);
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
