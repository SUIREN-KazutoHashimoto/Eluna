using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App;

public partial class SettingsWindow
{
    private ChatMessage? _message;
    private bool _reviewingChildren;
    private async void ReviewChildren(object? sender, CancelEventArgs e)
    {
        Window[] children = OwnedWindows.Cast<Window>().Where(w => w.IsVisible).ToArray();
        if (e.Cancel || children.Length == 0) { return; }
        e.Cancel = true; if (_reviewingChildren) { return; }
        _reviewingChildren = true;
        bool accepted = true;
        try { foreach (Window child in children) { child.Close(); if (!await UnsavedChanges.PendingClose(child) || child.IsVisible) { accepted = false; break; } } }
        finally { _reviewingChildren = false; }
        if (accepted) { await Dispatcher.InvokeAsync(Close); }
    }
    private bool CheckRoom()
    {
        if (_room is not null && _viewModel.SelectedConversation?.Id == _room.Id && _viewModel.Conversations.Contains(_room)) { return true; }
        SaveStateText.Text = "対象のチャンネルに戻るか、設定を開き直してください。変更は保持しています。"; return false;
    }
    public void ShowAccounts(Guid? account = null)
    {
        var editor = OwnedWindows.OfType<AgentsWindow>().FirstOrDefault(w => w.IsVisible) ?? new AgentsWindow(_viewModel) { Owner = this };
        if (account is Guid id) { editor.AgentList.SelectedItem = _viewModel.Agents.FirstOrDefault(a => a.Id == id); }
        if (!editor.IsVisible) { editor.Show(); } else { editor.Activate(); }
    }
    public void ShowMessage(ChatMessage message)
    {
        _message = message;
        MessageInfoBox.Text = message.Speaker + "\n" + message.SpeakerDetail + "\n" + message.ModeLabel + "\n" + message.Notice + "\n" + message.SkillsLabel;
        if (message.Work is not null) { DiscussionPanel.Work = message.Work; }
    }
    private void MessageKnowledge_Click(object sender, RoutedEventArgs e) { if (_message is { } message) { new KnowledgeWindow(message.Knowledge) { Owner = this }.Show(); } }
    private void MessageSkills_Click(object sender, RoutedEventArgs e) { if (_message is { } message) { new SkillUseWindow(message.Skills, _viewModel.DataDirectory) { Owner = this }.Show(); } }
    private void Accounts_Click(object sender, RoutedEventArgs e) => ShowAccounts();
    private void Account_Click(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: AgentAccount account }) { new AgentProfileWindow(_viewModel, account.Id) { Owner = this }.Show(); } }
    private void SkillSelection_Click(object sender, RoutedEventArgs e) { if (CheckRoom()) { new SkillPickerWindow(_viewModel) { Owner = this }.Show(); } }
    private void WorkOptions_Click(object sender, RoutedEventArgs e) { if (CheckRoom()) { new WorkOptionsWindow(_viewModel) { Owner = this }.ShowDialog(); } }
    private void SelfProfile_Click(object sender, RoutedEventArgs e) => new SelfProfileWindow(_viewModel) { Owner = this }.Show();
    private void InviteMembers_Click(object sender, RoutedEventArgs e) { if (CheckRoom() && _room is not null) { new InviteMembersWindow(_viewModel, _room) { Owner = this }.Show(); } }
    private void RoomSettings_Click(object sender, RoutedEventArgs e) { if (CheckRoom()) { new RoomWindow(_viewModel, _room) { Owner = this }.Show(); } }
    private void Permissions_Click(object sender, RoutedEventArgs e) { if (CheckRoom() && _room is not null) { new RoomPermissionsWindow(_viewModel, _room) { Owner = this }.Show(); } }
    private void ProjectSettings_Click(object sender, RoutedEventArgs e)
    { if (CheckRoom() && _viewModel.SelectedProject is { } project) { new ProjectWindow(_viewModel, project) { Owner = this }.Show(); } }
    private void Branch_Changed(object sender, SelectionChangedEventArgs e)
    { if (_fieldsReady && CheckRoom() && BranchSelector.SelectedItem is ConversationBranch branch) { _viewModel.SelectedBranch = branch; } }
    private void OpenTool(string tab, string? initial = null) { if (CheckRoom()) { Main?.OpenMediaTools(tab, initial); } }
    private void Vision_Click(object sender, RoutedEventArgs e) => OpenTool("vision");
    private void Image_Click(object sender, RoutedEventArgs e) => OpenTool("image");
    private void Document_Click(object sender, RoutedEventArgs e) => OpenTool("document");
    private void AnswerDocument_Click(object sender, RoutedEventArgs e) => OpenTool("document", _room?.Messages.LastOrDefault(m => m.Role == "assistant")?.Content);
    private void WorkActivity_Click(object sender, RoutedEventArgs e)
    { if (_room?.Messages.LastOrDefault(m => m.Work == DiscussionPanel.Work) is { } message) { new WorkWindow(_viewModel, message) { Owner = this }.Show(); } }
    private void DiscussionMembers_Click(object sender, RoutedEventArgs e)
    { if (CheckRoom() && DiscussionPanel.Work is { } work) { new RoomWindow(_viewModel, discussion: work) { Owner = this }.Show(); } }
    private async void Summary_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckRoom() || !_viewModel.CanEditConversation || _room is null) { return; }
        var prompt = new TextPromptWindow("会話の引き継ぎメモ", _room.Summary, true) { Owner = this };
        if (prompt.ShowDialog() == true) { _room.Summary = prompt.Result; await _viewModel.PersistSafelyAsync(); }
    }
    private async void Regenerate_Click(object sender, RoutedEventArgs e)
    { if (CheckRoom() && _room?.Messages.LastOrDefault(m => m.Role == "assistant") is { } message) { await _viewModel.RegenerateAsync(message); } }
    private async void Continue_Click(object sender, RoutedEventArgs e)
    { if (CheckRoom() && _room?.Messages.LastOrDefault(m => m.Role == "assistant") is { } message) { await _viewModel.ContinueAsync(message); } }
}
