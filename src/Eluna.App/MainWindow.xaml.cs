using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;
using Microsoft.Win32;

namespace Eluna.App;

public partial class MainWindow : Window
{
    private readonly ChatViewModel _viewModel;
    private readonly AppStore _store;
    private bool _composing, _followOutput = true, _closing, _shutdownStarted;
    private ScrollViewer? _messagesScroll;
    private string? _scrollKey;
    private string? _restoringReading;
    private int _scrollVersion;
    private readonly ReadingPositions _readingPositions;
    private PreparedBackup? _restoreBackup;
    private string? _restoreVerification;
    private readonly DispatcherTimer _readingSave = new() { Interval = TimeSpan.FromSeconds(1) };
    public ChatViewModel ViewModel => _viewModel;
    public MainWindow(AppStore store, DownloadQueue? downloads = null)
    {
        InitializeComponent(); _store = store; _readingPositions = new(store.Root); _viewModel = new(store, downloads); DataContext = _viewModel;
        Updates = new(store, _viewModel) { ApplyRequested = RequestUpdateAsync };
        InitializeMentions();
        InitializeNavigation();
        InitializeMessageExperience();
        InitializeComposer();
        InitializeContextMenus();
        InitializeMemberFocus();
        MaximizedWorkArea.Attach(this);
        _readingSave.Tick += async (_, _) => { _readingSave.Stop(); RememberReadingPosition(); await _readingPositions.SaveAsync(); };
        RootLayout.SizeChanged += (_, _) => { UpdateComposerHeight(); UpdateShellLayout(); };
        HeaderArea.SizeChanged += (_, _) => UpdateComposerHeight();
        ErrorBanner.SizeChanged += (_, _) => UpdateComposerHeight();
        UiDiagnostics.Attach(this, store.Trace);
        _viewModel.ScrollRequested += FollowOutput;
        _viewModel.OperationRequested += OpenOperation;
        _viewModel.MessageRequested += JumpToMessage;
        _viewModel.NavigationStarting += RememberReadingPosition;
        _viewModel.PlanReviewRequested += ReviewPlanAsync;
        _viewModel.SkillExecutionReviewRequested += ReviewSkillExecutionAsync;
        _viewModel.AppearanceChanged += () => { UiMotion.Configure(_viewModel.Settings.ReduceMotion); Appearance.Apply(_viewModel.Settings.Theme, _viewModel.Settings.UiScale); };
        TextCompositionManager.AddPreviewTextInputStartHandler(InputBox, (_, _) => { _composing = true; MentionPopup.IsOpen = false; RefreshMentionAdornment(); });
        TextCompositionManager.AddPreviewTextInputHandler(InputBox, (_, _) => { _composing = false; RefreshMentionAdornment(); });
        AllowDrop = true;
        InitializeFileDrop();
        DataObject.AddPastingHandler(InputBox, PasteAttachment);
        MessagesList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(MessagesScroll_Changed));
        MessagesList.PreviewMouseWheel += MessagesWheel;
        MessagesList.PreviewKeyDown += (_, e) => { if (e.Key is Key.PageUp or Key.Home or Key.Up) { _followOutput = false; } };
        MessagesList.PreviewMouseDown += (_, e) =>
        { for (DependencyObject? item = e.OriginalSource as DependencyObject; item is not null && item != MessagesList; item = item is Visual ? VisualTreeHelper.GetParent(item) : null) { if (item is System.Windows.Controls.Primitives.ScrollBar) { _followOutput = false; break; } } };
        Loaded += async (_, _) =>
        {
            RefreshMentionAdornment();
            await _readingPositions.LoadAsync(); string? latest = _readingPositions.Latest?.Key;
            _messagesScroll = FindChild<ScrollViewer>(MessagesList); await _viewModel.InitializeAsync();
            if (latest?.Split('/') is [var conversationId, var branchId] && Guid.TryParse(conversationId, out Guid id) && Guid.TryParse(branchId, out Guid branch) && _viewModel.Conversations.FirstOrDefault(c => c.Id == id && c.ProjectId == _viewModel.Settings.SelectedProjectId) is { } conversation)
            { _viewModel.SelectedConversation = conversation; _viewModel.SelectedBranch = conversation.Branches.FirstOrDefault(b => b.Id == branch) ?? conversation.ActiveBranch; }
            InputBox.Focus();
        };
        SourceInitialized += (_, _) => WindowPlacement.Restore(this, store.Root);
        _viewModel.BackupStarting += async () => { await _viewModel.LeaveVoiceAsync(); _readingSave.Stop(); RememberReadingPosition(); await _readingPositions.SaveAsync(); await WindowPlacement.SaveAsync(this, store.Root); };
        Closing += OnClosing; PreviewKeyDown += Window_KeyDown; Appearance.Attach(this);
    }
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) { return result; }
            if (FindChild<T>(child) is T nested) { return nested; }
        }
        return null;
    }
    private void UpdateComposerHeight()
    {
        double header = HeaderArea.ActualHeight + HeaderArea.Margin.Top + HeaderArea.Margin.Bottom;
        double error = ErrorBanner.IsVisible ? ErrorBanner.ActualHeight + ErrorBanner.Margin.Top + ErrorBanner.Margin.Bottom : 0;
        ComposerArea.MaxHeight = Math.Clamp(RootLayout.ActualHeight - header - error - 114, 160, 400);
    }
    private void Models_Click(object sender, RoutedEventArgs e)
    {
        Guid? accountId = _viewModel.SelectedRecipient?.Id ?? _viewModel.SelectedConversation?.Room?.DefaultAgentId;
        ShowUnique(() =>
        {
            var browser = new ModelWindow(_store, _viewModel.Downloads, _viewModel.ComputeChoices, _viewModel.Settings.Compute, _viewModel.EffectiveContext, _viewModel) { Owner = this };
            browser.Closed += async (_, _) =>
            {
                await _viewModel.RefreshModelsAsync(browser.DownloadedModel);
                if (browser.DownloadedModel is { } model && _viewModel.Agents.FirstOrDefault(a => a.Id == accountId) is { } account) { await _viewModel.SaveAgentAsync(account with { ModelId = model.Id }); }
            }; return browser;
        });
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private static T ShowUnique<T>(Func<T> create) where T : Window
    {
        T? existing = Application.Current.Windows.OfType<T>().FirstOrDefault(w => w.IsVisible);
        if (existing is not null) { if (existing.WindowState == WindowState.Minimized) { existing.WindowState = WindowState.Normal; } existing.Activate(); return existing; }
        T window = create(); window.Show(); return window;
    }
    internal void OpenModels() => Models_Click(this, new RoutedEventArgs());
    internal void OpenModelManager() => ManageModels_Click(this, new RoutedEventArgs());
    internal void OpenData() => new DataWindow(_viewModel) { Owner = this }.ShowDialog();
    internal void RestorePreparedBackup(PreparedBackup backup, string? verification = null) { _restoreBackup = backup; _restoreVerification = verification; Close(); }
    internal void OpenDownloads() => ShowUnique(() => new DownloadsWindow(_viewModel.Downloads, _viewModel) { Owner = this });
    private void OpenMedia(string tab = "document", string? initial = null)
    {
        MediaWindow? existing = Application.Current.Windows.OfType<MediaWindow>().FirstOrDefault(w => w.ScopeId == _viewModel.Settings.SelectedProjectId);
        if (existing is not null) { existing.SelectTab(tab, initial); existing.Activate(); return; }
        new MediaWindow(_store, _viewModel, tab, initial) { Owner = this }.Show();
    }
    private void Reference_Clicked(object? sender, ReferenceEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChatMessage message }) { return; }
        if (e.Kind == "K") { var matches = message.Knowledge.Where(k => k.Number == e.Number).ToArray(); if (matches.Length > 0) { new KnowledgeWindow(matches) { Owner = this }.Show(); } }
        else if (message.Work?.Sources.FirstOrDefault(source => source.Number == e.Number) is { } source) { new SourceWindow(source) { Owner = this }.Show(); }
    }
    private void SkillUse_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { DataContext: ChatMessage message }) { new SkillUseWindow(message.Skills, _viewModel.DataDirectory) { Owner = this }.Show(); } }
    private async Task<bool> ReviewSkillExecutionAsync(SkillExecutionRequest request, CancellationToken token)
    {
        if (token.IsCancellationRequested) { return false; }
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new SkillExecutionWindow(request) { Owner = this, ShowActivated = false };
        dialog.Closed += (_, _) => completion.TrySetResult(dialog.Accepted);
        using CancellationTokenRegistration registration = token.Register(() => Dispatcher.BeginInvoke(() => { if (dialog.IsVisible) { dialog.Close(); } }));
        dialog.Show(); return await completion.Task;
    }
    private void ProjectMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !_viewModel.CanChange) { return; }
        var menu = new ContextMenu { PlacementTarget = button };
        void Item(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("サーバーを作成", async () =>
        {
            var prompt = new TextPromptWindow("サーバーを作成", "新しいサーバー", false, 80) { Owner = this };
            if (prompt.ShowDialog() == true && !string.IsNullOrWhiteSpace(prompt.Result)) { await _viewModel.SaveProjectAsync(new(Guid.NewGuid(), prompt.Result.Trim(), "", "project-only", false, [], [])); }
        });
        Item("チャンネル・DMを作成", () => NewRoom_Click(sender, e));
        if (_viewModel.SelectedProject is { } server) { Item("サーバー設定・資料・メモリ", () => new ProjectWindow(_viewModel, server) { Owner = this }.Show()); }
        Item("チャンネルの設定", () => OpenSettings(7));
        Item(_viewModel.ShowArchived ? "通常のチャンネルを表示" : "保管済みを表示", () => _viewModel.ShowArchived = !_viewModel.ShowArchived);
        Item("ユーザー設定", () => OpenSettings());
        menu.IsOpen = true;
    }
    private void NewRoom_Click(object sender, RoutedEventArgs e) => new RoomWindow(_viewModel, basicOnly: true) { Owner = this }.Show();
    private void ReplyMessage_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { DataContext: ChatMessage message }) { _viewModel.ReplyTo(message); InputBox.Focus(); } }
    private void ClearReply_Click(object sender, RoutedEventArgs e) => _viewModel.ReplyTo(null);
    private void JumpReply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChatMessage { Identity.ReplyTo: Guid id } }) { return; }
        Conversation? room = _viewModel.Conversations.FirstOrDefault(c => c.Branches.Any(b => b.Messages.Any(m => m.Id == id || m.Work?.Turns.Any(t => (t.Id ?? WorkspaceService.StableId(m.Work.Id, t.Round + ":" + t.Role)) == id) == true)));
        if (room is not null) { _viewModel.OpenMessage(room.Id, id); }
    }
    private void MessageMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChatMessage message } button || _viewModel.SelectedConversation is not { } room) { return; }
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var thread = new MenuItem { Header = "この発言からスレッドを作成", IsEnabled = room.Room?.Kind != "thread" };
        Guid branch = room.ActiveBranch.Id;
        button.ContextMenu = menu;
        var pin = new MenuItem { Header = message.PinnedMessage ? "発言のピン留めを解除" : "発言をピン留め", IsEnabled = message.HasContent }; pin.Click += async (_, _) => await ToggleMessagePinAsync(room.Id, branch, message.Id); menu.Items.Add(pin);
        var unread = new MenuItem { Header = "この発言を未読にする" }; unread.Click += (_, _) => MarkMessageUnread(room, branch, message); menu.Items.Add(unread);
        thread.Click += (_, _) => new RoomWindow(_viewModel, room, origin: message, basicOnly: true) { Owner = this }.Show(); menu.Items.Add(thread);
        var info = new MenuItem { Header = "メッセージの詳細" }; info.Click += (_, _) => { OpenSettings(7); Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault(w => w.Owner == this)?.ShowMessage(message); }; menu.Items.Add(info);
        menu.IsOpen = true;
    }
    private async void OpenArtifact_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChatMessage { Artifact: { } artifact } }) { return; }
        try
        {
            string path = System.IO.Path.GetFullPath(artifact.ResultPath);
            if (!path.StartsWith(System.IO.Path.Combine(_store.Root, "Generated") + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path))
            { throw new System.IO.FileNotFoundException("生成物が見つかりません。バックアップの復元先または保存場所を確認してください。"); }
            MediaJob? job = _viewModel.MediaJobs.FirstOrDefault(j => j.Id == artifact.JobId);
            if (job is null)
            {
                if (new System.IO.FileInfo(path).Length > 4_000_000 && artifact.Kind != "image") { throw new System.IO.InvalidDataException("本文が大きすぎるため、この画面では開けません。"); }
                job = new MediaJob(artifact.Title, new(artifact.Kind, "", "", null, path)) { Id = artifact.JobId, State = "complete", Detail = "保存した生成物", Text = artifact.Kind == "image" ? "" : await System.IO.File.ReadAllTextAsync(path) };
            }
            new MediaResultWindow(job, _store.Root) { Owner = this }.Show();
        }
        catch (Exception ex) { _viewModel.Error = ex.Message; }
    }
    private void Knowledge_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatMessage answer }) { new KnowledgeWindow(answer.Knowledge) { Owner = this }.Show(); }
    }
    private void ManageModels_Click(object sender, RoutedEventArgs e)
    {
        ShowUnique(() => { var window = new ModelsWindow(_store, _viewModel) { Owner = this }; window.Closed += async (_, _) => await _viewModel.RequestModelsRefreshAsync(); return window; });
    }
    private void Inferences_Click(object sender, RoutedEventArgs e) => new InferenceQueueWindow(_viewModel) { Owner = this }.Show();
    private void Downloads_Click(object sender, RoutedEventArgs e) => new DownloadsWindow(_viewModel.Downloads, _viewModel) { Owner = this }.Show();
    private void MediaMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) { return; }
        var menu = new ContextMenu { PlacementTarget = button };
        void Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("ファイルをアップロード…", async () => { var picker = new OpenFileDialog { Filter = AttachmentService.Filter, Multiselect = true }; if (picker.ShowDialog(this) == true) { await _viewModel.AttachFilesAsync(picker.FileNames); } });
        if (menu.Items[0] is MenuItem attachment) { attachment.IsEnabled = _viewModel.CanChange; attachment.ToolTip = "実行中の推論・準備が終わると添付できます。別の依頼は送信・予約できます。"; ToolTipService.SetShowOnDisabled(attachment, true); }
        menu.IsOpen = true;
    }
    private async void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { DataContext: ChatAttachment attachment }) { await _viewModel.RemoveAttachmentAsync(attachment); } }
    private void AttachmentPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChatAttachment attachment }) { return; }
        FrameworkElement content;
        if (attachment.ImageBase64 is not null)
        {
            using var stream = new MemoryStream(Convert.FromBase64String(attachment.ImageBase64));
            var image = new System.Windows.Media.Imaging.BitmapImage(); image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            content = new ScrollViewer { Content = new Image { Source = image, Stretch = Stretch.Uniform } };
        }
        else { content = new TextBox { Text = attachment.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(20) }; }
        var preview = new Window { Title = attachment.Name + " · 添付の確認", Owner = this, Width = 820, Height = 700, Content = content }; Appearance.Attach(preview); preview.Show();
    }
    private async void PasteAttachment(object sender, DataObjectPastingEventArgs e)
    {
        string? path = null;
        try
        {
            if (!_viewModel.CanChange)
            {
                if (e.DataObject.GetDataPresent(DataFormats.FileDrop) || e.DataObject.GetDataPresent(DataFormats.Bitmap))
                { e.CancelCommand(); _viewModel.Error = "画像・ファイルの貼り付けは準備処理が終わってから利用できます。"; }
                return;
            }
            if (e.DataObject.GetData(DataFormats.FileDrop) is string[] paths) { e.CancelCommand(); await _viewModel.AttachFilesAsync(paths); return; }
            if (e.DataObject.GetData(DataFormats.Bitmap) is not System.Windows.Media.Imaging.BitmapSource bitmap) { return; }
            e.CancelCommand(); path = Path.Combine(Path.GetTempPath(), "Eluna-clipboard-" + Guid.NewGuid().ToString("N") + ".png");
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            await using (var stream = new FileStream(path, FileMode.CreateNew)) { encoder.Save(stream); }
            await _viewModel.AttachFilesAsync([path]);
        }
        catch (Exception ex) { _viewModel.Error = DiagnosticsService.Explain(ex); }
        finally { try { if (path is not null && File.Exists(path)) { File.Delete(path); } } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }
    private void WorkActivity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatMessage { Work: not null } answer }) { OpenSettings(9); Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault(w => w.Owner == this)?.ShowMessage(answer); }
    }
    private void OpenOperation(ChatMessage answer)
    { OpenSettings(9); Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault(w => w.Owner == this)?.ShowMessage(answer); }
    private async Task<bool> ReviewPlanAsync(WorkSession work, CancellationToken token)
    {
        if (token.IsCancellationRequested) { return false; }
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new WorkPlanWindow(work, modeless: true) { Owner = this, ShowActivated = false };
        dialog.Closed += (_, _) => completion.TrySetResult(dialog.Accepted);
        using CancellationTokenRegistration registration = token.Register(() => Dispatcher.BeginInvoke(() => { if (dialog.IsVisible) { dialog.Close(); } }));
        dialog.Show();
        return await completion.Task;
    }
    private void Data_Click(object sender, RoutedEventArgs e) => new DataWindow(_viewModel) { Owner = this }.ShowDialog();
    private void Sidebar_Click(object sender, RoutedEventArgs e) => _viewModel.ToggleSidebar();
    private void Latest_Click(object sender, RoutedEventArgs e) => FollowOutput(true);
    private void DismissError_Click(object sender, RoutedEventArgs e) => _viewModel.Error = "";
    private async void DeleteChat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Conversation conversation }) { await DeleteChatAsync(conversation); }
    }
    private async Task DeleteChatAsync(Conversation conversation)
    {
        if (MessageBox.Show(this, $"「{conversation.Title}」を削除しますか？", "会話を削除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await _viewModel.DeleteConversationAsync(conversation);
        }
    }
    private void HistoryMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Conversation conversation } button) { return; }
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        button.ContextMenu = menu;
        void Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("名前を変更", async () => { var dialog = new TextPromptWindow("会話の名前", conversation.Title) { Owner = this }; if (dialog.ShowDialog() == true) { await _viewModel.RenameConversationAsync(conversation, dialog.Result); } });
        Item("アクセス権", () => new RoomPermissionsWindow(_viewModel, conversation) { Owner = this }.Show());
        Item("メンバーを招待", () => OpenInvitations(conversation));
        Item("既読にする", () => _viewModel.MarkRead(conversation.Id));
        Item(conversation.Experience.Muted ? "ミュートを解除" : "未読一覧からミュート", async () => await _viewModel.SetMutedAsync(conversation.Id, !conversation.Experience.Muted));
        Item(conversation.IsPinned ? "ピン留めを解除" : "ピン留め", async () => await PinChannelWithUndoAsync(conversation));
        Item(conversation.IsArchived ? "保管から戻す" : "アーカイブ", async () => await ArchiveWithUndoAsync(conversation));
        var move = new MenuItem { Header = "プロジェクトへ移動" };
        foreach (ProjectChoice choice in _viewModel.ProjectChoices)
        {
            var destination = new MenuItem { Header = choice.Name, IsEnabled = choice.Id != conversation.ProjectId };
            destination.Click += async (_, _) =>
            {
                if (MessageBox.Show(this, $"「{choice.Name}」へ会話を移動します。本文・添付・下書き・個別指示・生成時の参照記録には以前の情報が残ります。元プロジェクトの資料・保存メモリは移さず、会話の引き継ぎメモと以前の調査の再開を無効にします。移動しますか？", "会話の移動", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { return; }
                try { await _viewModel.MoveConversationAsync(conversation, choice.Id); } catch (Exception ex) { _viewModel.Error = ex.Message; }
            };
            move.Items.Add(destination);
        }
        menu.Items.Add(move);
        Item("削除", async () => await DeleteChatAsync(conversation)); menu.IsOpen = true;
    }
    private void ChatMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) { return; }
        var menu = new ContextMenu { PlacementTarget = button };
        var settings = new MenuItem { Header = "チャンネル設定" }; settings.Click += (_, _) => OpenSettings(7); menu.Items.Add(settings);
        var pins = new MenuItem { Header = "ピン留めした発言" }; pins.Click += (_, _) => OpenMessageCenter("pins"); menu.Items.Add(pins);
        var search = new MenuItem { Header = "この会話の発言を検索" }; search.Click += (_, _) => OpenMessageCenter("search"); menu.Items.Add(search);
        var read = new MenuItem { Header = "このチャンネルを既読にする" }; read.Click += ReadSelected_Click; menu.Items.Add(read);
        var invite = new MenuItem { Header = "メンバーを招待" }; invite.Click += (_, _) => { if (_viewModel.SelectedConversation is { } room) { OpenInvitations(room); } }; menu.Items.Add(invite);
        var permissions = new MenuItem { Header = "アクセス権" }; permissions.Click += (_, _) => { if (_viewModel.SelectedConversation is { } room) { new RoomPermissionsWindow(_viewModel, room) { Owner = this }.Show(); } }; menu.Items.Add(permissions);
        var archive = new MenuItem { Header = "保管済みを表示", IsCheckable = true, IsChecked = _viewModel.ShowArchived }; archive.Click += (_, _) => _viewModel.ShowArchived = archive.IsChecked; menu.Items.Add(archive);
        var export = new MenuItem { Header = "この会話をMarkdownで書き出す" };
        export.Click += async (_, _) =>
        {
            if (_viewModel.SelectedConversation is not { } conversation) { return; }
            var picker = new SaveFileDialog { Filter = "Markdown (*.md)|*.md", FileName = "Eluna-chat.md" };
            if (picker.ShowDialog(this) == true) { try { await File.WriteAllTextAsync(picker.FileName, "# " + conversation.Title + "\n\n" + string.Join("\n\n", conversation.Messages.Select(m => "## " + m.Speaker + "\n\n" + m.Content))); } catch (IOException) { _viewModel.Error = "会話を書き出せませんでした。保存先を確認してください。"; } }
        };
        menu.Items.Add(export); menu.IsOpen = true;
    }
    private void TeamActivity_Click(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: ChatMessage message }) { new TeamWindow(_viewModel, message) { Owner = this }.Show(); } }
    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatMessage message }) { CopyMessage(message); }
    }
    private void CopyMessage(ChatMessage message)
    {
        try { Clipboard.SetText(message.Content); ShowToast("発言をコピーしました。"); }
        catch (System.Runtime.InteropServices.COMException) { _viewModel.Error = "コピーできませんでした。もう一度試してください。"; }
    }
    private async void EditMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChatMessage message }) { return; }
        var dialog = new TextPromptWindow("質問を編集して別の回答を生成", message.Content, true) { Owner = this };
        if (dialog.ShowDialog() == true) { await _viewModel.EditAndGenerateAsync(message, dialog.Result); }
    }
    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (MentionKey(e)) { return; }
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.Enter && !_composing && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && (!_viewModel.Settings.ControlEnterToSend || control))
        {
            if (_viewModel.SendCommand.CanExecute(null)) { e.Handled = true; _viewModel.SendCommand.Execute(null); }
        }
    }
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_composing) { return; }
        if (Keyboard.FocusedElement is MenuItem || e.OriginalSource is MenuItem) { return; }
        if (ComposerShortcut(e)) { return; }
        if (InputBox.IsKeyboardFocusWithin && MentionKey(e)) { return; }
        if (NavigationKey(e)) { return; }
        if (e.Key == Key.Escape)
        {
            if (MembersPopup.IsOpen) { MembersPopup.IsOpen = false; MembersButton.Focus(); e.Handled = true; return; }
            if (e.OriginalSource is ComboBox { IsDropDownOpen: true }) { return; }
            if (_viewModel.CanStopSelected) { _viewModel.StopCommand.Execute(null); e.Handled = true; }
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.N) { _viewModel.NewChatCommand.Execute(null); InputBox.Focus(); e.Handled = true; }
            if (e.Key == Key.B) { _viewModel.ToggleSidebar(); e.Handled = true; }
            if (e.Key == Key.F) { if (!_viewModel.SidebarVisible) { _viewModel.ToggleSidebar(); } HistorySearchBox.Focus(); e.Handled = true; }
            if (e.Key == Key.End && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase) { FollowOutput(true); e.Handled = true; }
        }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ChatMessage? answer = _viewModel.SelectedConversation?.Messages.LastOrDefault(m => m.Role == "assistant");
            if (e.Key == Key.R && answer is not null && _viewModel.CanGenerate) { await _viewModel.RegenerateAsync(answer); e.Handled = true; }
            if (e.Key == Key.C && answer is not null) { CopyMessage(answer); e.Handled = true; }
        }
    }
    private void MessagesWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { return; }
        _messagesScroll ??= FindChild<ScrollViewer>(MessagesList);
        if (_messagesScroll is null || _messagesScroll.ScrollableHeight <= 0) { return; }
        _viewModel.Trace.Write("debug", "ui", "messages.scroll", "delta=" + e.Delta);
        _followOutput = false;
        double unit = SystemParameters.WheelScrollLines < 0 ? _messagesScroll.ViewportHeight : SystemParameters.WheelScrollLines * _viewModel.ChatLineHeight;
        _messagesScroll.ScrollToVerticalOffset(_messagesScroll.VerticalOffset - e.Delta / 120.0 * unit);
        e.Handled = true;
    }
    internal bool FollowingOutput => _followOutput;
    private void MessagesScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _messagesScroll) || _messagesScroll is null) { return; }
        if (_restoringReading == _scrollKey) { return; }
        if (e.ExtentHeightChange == 0 || e.VerticalChange < 0) { _followOutput = _messagesScroll.ScrollableHeight - _messagesScroll.VerticalOffset < 80; }
        LatestButton.Visibility = _followOutput ? Visibility.Collapsed : Visibility.Visible;
        RefreshReadingState();
        _readingSave.Stop(); _readingSave.Start();
    }
    private void RememberReadingPosition() { if (_scrollKey is not null && _messagesScroll is not null && _restoringReading != _scrollKey) { _readingPositions.Set(_scrollKey, _messagesScroll.VerticalOffset, _followOutput); } }
    internal bool ReadingPositionPending => _restoringReading is not null;
    private async Task RestoreReadingAsync(ReadingPosition position, int version)
    {
        int stable = 0;
        try
        {
            // ScrollViewer processes seek commands on later layout frames. Recycled item height
            // estimates may adjust the first seek; wait for measured frames before saving it.
            for (int attempt = 0; attempt < 12 && stable < 2; attempt++)
            {
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                if (_scrollVersion != version) { return; }
                MessagesList.UpdateLayout(); _messagesScroll?.ScrollToVerticalOffset(position.Offset);
                await Task.Delay(25);
                if (_scrollVersion != version) { return; }
                MessagesList.UpdateLayout();
                stable = _messagesScroll is not null && Math.Abs(_messagesScroll.VerticalOffset - Math.Min(position.Offset, _messagesScroll.ScrollableHeight)) < 1 ? stable + 1 : 0;
            }
        }
        finally
        {
            if (_scrollVersion == version) { _followOutput = position.Follow; _restoringReading = null; LatestButton.Visibility = _followOutput ? Visibility.Collapsed : Visibility.Visible; RefreshReadingState(); }
        }
    }
    private void FollowOutput(bool force)
    {
        string key = $"{_viewModel.SelectedConversation?.Id}/{_viewModel.SelectedBranch?.Id}";
        if (_scrollKey != key)
        {
            _scrollKey = key; _restoringReading = null; _scrollVersion++; int restoreVersion = _scrollVersion;
            if (_viewModel.HistorySearch.Trim().Length == 0 && _readingPositions.Find(key) is { } position)
            {
                _followOutput = position.Follow; _restoringReading = key;
                _ = RestoreReadingAsync(position, restoreVersion); return;
            }
            _followOutput = true;
        }
        if (_restoringReading == key) { return; }
        int version = _scrollVersion;
        _followOutput |= force;
        if (_viewModel.HistorySearch.Trim().Length > 0 && _viewModel.SelectedConversation?.Messages.FirstOrDefault(m => m.Content.Contains(_viewModel.HistorySearch.Trim(), StringComparison.OrdinalIgnoreCase)) is ChatMessage match)
        { _followOutput = false; Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { if (_scrollVersion != version) { return; } MessagesList.ScrollIntoView(match); LatestButton.Visibility = Visibility.Visible; }); return; }
        if (_followOutput)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (_scrollVersion != version || !_followOutput) { return; }
                _messagesScroll ??= FindChild<ScrollViewer>(MessagesList);
                if (_viewModel.Timeline.LastOrDefault() is ChatMessage last) { MessagesList.ScrollIntoView(last); }
                _messagesScroll?.ScrollToEnd(); LatestButton.Visibility = Visibility.Collapsed;
                RefreshReadingState();
            });
        }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) { return; }
        e.Cancel = true; if (_shutdownStarted) { return; }
        foreach (Window child in OwnedWindows.Cast<Window>().ToArray())
        {
            child.Close(); bool closed = child is MediaWindow media ? await media.PendingClose : child is DataWindow data ? await data.PendingClose : await UnsavedChanges.PendingClose(child);
            if (!closed || child.IsVisible)
            {
                if (_restoreBackup is { } canceled) { _restoreBackup = null; await Task.Run(() => BackupArchive.DeleteStage(canceled.Directory, _store.BackupsDirectory)); }
                return;
            }
        }
        if (_updatePlan is not null) { if (!_viewModel.CanApplyUpdate) { _updatePlan = null; return; } _viewModel.FreezeForUpdate(); }
        Updates.Dispose(); _shutdownStarted = true; IsEnabled = false;
        _readingSave.Stop(); RememberReadingPosition(); await _readingPositions.SaveAsync();
        await _viewModel.ShutdownAsync(); await WindowPlacement.SaveAsync(this, _store.Root); _viewModel.Dispose();
        if (_restoreBackup is { } restore)
        {
            Title = "バックアップを復元しています · Eluna";
            try { await BackupArchive.ApplyAsync(_store, restore, new Progress<BackupProgress>(p => Title = $"復元前のデータを退避中 {p.Percent:N0}% · Eluna")); }
            catch (Exception ex)
            {
                if (_restoreVerification is not null) { await File.WriteAllTextAsync(Path.Combine(_restoreVerification, "restore-error.txt"), ex.ToString()); }
                else { MessageBox.Show(this, "復元を完了できませんでした。起動時にも復元記録から元のデータへの回復を試みます。\n" + ex.Message, "バックアップの復元", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            try { App.Restart(_store.Root, _restoreVerification); }
            catch (Exception ex)
            {
                if (_restoreVerification is not null) { await File.WriteAllTextAsync(Path.Combine(_restoreVerification, "restart-error.txt"), ex.ToString()); }
                else { MessageBox.Show(this, "再起動できませんでした。Eluna.exeをもう一度起動してください。\n" + ex.Message, "再起動", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }
        if (_updatePlan is not null) { try { UpdateWorker.Launch(_updatePlan); } catch (Exception error) { MessageBox.Show(this, "更新を開始できませんでした。現在の版はそのまま使えます。\n" + error.Message, "Elunaの更新"); } }
        _closing = true; Close();
    }
}
