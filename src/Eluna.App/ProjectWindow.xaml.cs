using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;
using Microsoft.Win32;

namespace Eluna.App;

public partial class ProjectWindow : Window
{
    private readonly ChatViewModel _chat;
    private readonly Guid _id;
    private readonly bool _existing;
    private readonly ObservableCollection<KnowledgeDocument> _documents = [];
    private bool _importing, _closeAfterImport;
    private string _baseline = "";
    public ProjectWindow(ChatViewModel chat, KnowledgeProject? project = null)
    {
        InitializeComponent(); _chat = chat; _id = project?.Id ?? Guid.NewGuid(); _existing = project is not null; Appearance.Attach(this);
        Heading.Text = _existing ? "サーバーを管理" : "サーバーを作成";
        NameBox.Text = project?.Name ?? "新しいサーバー"; InstructionBox.Text = project?.Instruction ?? "";
        ScopeBox.ItemsSource = new[] { "サーバー限定（推奨）", "共通メモリも使う" }; ScopeBox.SelectedIndex = project?.MemoryMode == "shared" ? 1 : 0; ScopeBox.IsEnabled = !_existing;
        ReferenceChatsBox.IsChecked = project?.ReferenceChats ?? true; AcceptAgentMemoryBox.IsChecked = project?.AcceptAgentMemory == true;
        foreach (KnowledgeDocument document in project?.Documents ?? []) { _documents.Add(document); }
        DocumentsList.ItemsSource = _documents; DocumentsList.SelectedIndex = _documents.Count > 0 ? 0 : -1;
        ProjectMemoryEditor.LoadNotes(project?.Memories ?? []); DeleteButton.Visibility = _existing ? Visibility.Visible : Visibility.Collapsed;
        Closing += (_, e) => { if (_importing) { e.Cancel = true; _closeAfterImport = true; _chat.StopCommand.Execute(null); } };
        _baseline = Snapshot(); UnsavedChanges.Protect(this, () => !_importing && Snapshot() != _baseline, SaveDraftAsync);
        AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => ShowDirty()));
        ProjectMemoryEditor.Changed += (_, _) => ShowDirty(); _documents.CollectionChanged += (_, _) => ShowDirty();
    }
    private void Scope_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ScopeText is null) { return; }
        ScopeText.Text = ScopeBox.SelectedIndex == 1 ? "共通指示・共通の保存メモリも使います。別サーバーの会話・資料・メモリは使いません。" :
            "このサーバーの会話・資料・保存メモリだけを使います。共通指示・共通メモリ・サーバー外の会話は参照しません。";
    }
    internal KnowledgeProject CreateDraft() => new(_id, NameBox.Text.Trim(), InstructionBox.Text, ScopeBox.SelectedIndex == 1 ? "shared" : "project-only",
        ReferenceChatsBox.IsChecked == true, [.. _documents], ProjectMemoryEditor.GetNotes(), _chat.Projects.FirstOrDefault(p => p.Id == _id)?.UseCommonSkills == true, AcceptAgentMemoryBox.IsChecked == true);
    private string Snapshot() => JsonSerializer.Serialize(CreateDraft(), AppStore.JsonOptions);
    private void ShowDirty() { if (_baseline.Length > 0) { Title = Snapshot() != _baseline ? "サーバー · 変更あり · Eluna" : "サーバー · Eluna"; } }
    private async void Save_Click(object sender, RoutedEventArgs e)
    { if (await SaveDraftAsync()) { Close(); } }
    internal async Task<bool> SaveDraftAsync()
    {
        SaveButton.IsEnabled = false;
        try { await _chat.SaveProjectAsync(CreateDraft()); _baseline = Snapshot(); ShowDirty(); return true; }
        catch (Exception ex) { StatusText.Text = ex.Message; return false; }
        finally { SaveButton.IsEnabled = true; }
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        int chats = _chat.Conversations.Count(c => c.ProjectId == _id);
        if (MessageBox.Show(this, $"このサーバーと所属する会話{chats}件、資料、保存メモリを削除します。バックアップには残る場合があります。削除しますか？", "サーバーの削除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { return; }
        try { await _chat.DeleteProjectAsync(_id); _baseline = Snapshot(); Close(); } catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private async void AddDocuments_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = AttachmentService.Filter, Multiselect = true };
        if (picker.ShowDialog(this) == true) { await ImportFilesAsync(picker.FileNames); }
    }
    internal async Task ImportFilesAsync(string[] paths)
    {
        if (_importing) { return; }
        if (_documents.Count + paths.Length > 64) { StatusText.Text = "資料は64件までです。"; return; }
        _importing = true; ProjectTabs.IsEnabled = SaveButton.IsEnabled = DeleteButton.IsEnabled = false; StopButton.Visibility = Visibility.Visible;
        try
        {
            List<KnowledgeDocument>? imported = null;
            await _chat.RunMediaOperationAsync(async token => imported = await _chat.ImportKnowledgeDocumentsAsync(paths, token));
            foreach (KnowledgeDocument document in imported ?? []) { _documents.Add(document); }
            DocumentsList.SelectedItem = _documents.LastOrDefault();
            StatusText.Text = "資料を読み取りました。内容と参照の有効・無効を確認して保存してください。";
        }
        catch (OperationCanceledException) { StatusText.Text = "資料の読み取りを停止しました。追加前の状態を保持しています。"; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally
        {
            _importing = false; ProjectTabs.IsEnabled = SaveButton.IsEnabled = DeleteButton.IsEnabled = true; StopButton.Visibility = Visibility.Collapsed;
            if (_closeAfterImport) { Close(); }
        }
    }
    private void Stop_Click(object sender, RoutedEventArgs e) => _chat.StopCommand.Execute(null);
    private void Document_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DocumentText is null) { return; }
        KnowledgeDocument? document = DocumentsList.SelectedItem as KnowledgeDocument;
        DocumentText.Text = document?.Text ?? ""; DocumentText.ScrollToHome();
        EditDocumentButton.IsEnabled = ToggleDocumentButton.IsEnabled = RemoveDocumentButton.IsEnabled = document is not null;
        ToggleDocumentButton.Content = document?.Enabled == true ? "参照を無効にする" : document?.VisionExtracted == true ? "確認して参照を有効にする" : "参照を有効にする";
    }
    private void ReplaceDocument(KnowledgeDocument before, KnowledgeDocument after) { _documents[_documents.IndexOf(before)] = after; DocumentsList.SelectedItem = after; }
    private void EditDocument_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentsList.SelectedItem is not KnowledgeDocument document) { return; }
        var editor = new TextPromptWindow("資料の本文（60,000文字まで）", document.Text, true, 61000) { Owner = this };
        if (editor.ShowDialog() == true && !string.IsNullOrWhiteSpace(editor.Result)) { ReplaceDocument(document, document with { Text = editor.Result }); }
    }
    private void ToggleDocument_Click(object sender, RoutedEventArgs e) { if (DocumentsList.SelectedItem is KnowledgeDocument document) { ReplaceDocument(document, document with { Enabled = !document.Enabled }); } }
    private void RemoveDocument_Click(object sender, RoutedEventArgs e) { if (DocumentsList.SelectedItem is KnowledgeDocument document) { _documents.Remove(document); } }
}
