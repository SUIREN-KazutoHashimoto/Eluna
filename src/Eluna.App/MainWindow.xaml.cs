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
    private readonly ReadingPositions _readingPositions;
    private PreparedBackup? _restoreBackup;
    private string? _restoreVerification;
    private readonly DispatcherTimer _readingSave = new() { Interval = TimeSpan.FromSeconds(1) };
    public ChatViewModel ViewModel => _viewModel;
    public MainWindow(AppStore store, DownloadQueue? downloads = null)
    {
        InitializeComponent(); _store = store; _readingPositions = new(store.Root); _viewModel = new(store, downloads); DataContext = _viewModel;
        _readingSave.Tick += async (_, _) => { _readingSave.Stop(); RememberReadingPosition(); await _readingPositions.SaveAsync(); };
        RootLayout.SizeChanged += (_, _) => UpdateComposerHeight();
        HeaderArea.SizeChanged += (_, _) => UpdateComposerHeight();
        ErrorBanner.SizeChanged += (_, _) => UpdateComposerHeight();
        ComposerArea.SizeChanged += (_, _) => UpdateComposerTools();
        ProcessContent.Content = new ProcessListPanel(_viewModel);
        _viewModel.ScrollRequested += FollowOutput;
        _viewModel.NavigationStarting += RememberReadingPosition;
        _viewModel.PlanReviewRequested += ReviewPlanAsync;
        _viewModel.SkillExecutionReviewRequested += ReviewSkillExecutionAsync;
        _viewModel.AppearanceChanged += () => Appearance.Apply(_viewModel.Settings.Theme, _viewModel.Settings.UiScale);
        TextCompositionManager.AddPreviewTextInputStartHandler(InputBox, (_, _) => _composing = true);
        TextCompositionManager.AddPreviewTextInputHandler(InputBox, (_, _) => _composing = false);
        AllowDrop = true;
        DragOver += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = _viewModel.CanChange ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; } };
        Drop += async (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) { e.Handled = true; await _viewModel.AttachFilesAsync(paths); } };
        DataObject.AddPastingHandler(InputBox, PasteAttachment);
        MessagesList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(MessagesScroll_Changed));
        Loaded += async (_, _) =>
        {
            await _readingPositions.LoadAsync(); string? latest = _readingPositions.Latest?.Key;
            _messagesScroll = FindChild<ScrollViewer>(MessagesList); await _viewModel.InitializeAsync();
            if (latest?.Split('/') is [var conversationId, var branchId] && Guid.TryParse(conversationId, out Guid id) && Guid.TryParse(branchId, out Guid branch) && _viewModel.Conversations.FirstOrDefault(c => c.Id == id && c.ProjectId == _viewModel.Settings.SelectedProjectId) is { } conversation)
            { _viewModel.SelectedConversation = conversation; _viewModel.SelectedBranch = conversation.Branches.FirstOrDefault(b => b.Id == branch) ?? conversation.ActiveBranch; }
            InputBox.Focus();
        };
        SourceInitialized += (_, _) => WindowPlacement.Restore(this, store.Root);
        _viewModel.BackupStarting += async () => { _readingSave.Stop(); RememberReadingPosition(); await _readingPositions.SaveAsync(); await WindowPlacement.SaveAsync(this, store.Root); };
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
    private void UpdateComposerTools()
    {
        bool compact = ComposerArea.ActualWidth < 620;
        ComposerSkillsButton.Visibility = ComposerWorkOptionsButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ComposerMoreButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        WorkModeSelector.Width = compact ? 160 : 172;
    }
    private void ComposerMore_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ComposerMoreButton };
        var skills = new MenuItem { Header = _viewModel.SkillsLabel + "…" };
        skills.Click += (_, _) => SkillPicker_Click(sender, e); menu.Items.Add(skills);
        var options = new MenuItem { Header = _viewModel.SelectedWorkMode.Id == "debate" ? "ディベートの参加者と上限…" : "検索・調査・議論の設定…" };
        options.Click += (_, _) => WorkOptions_Click(sender, e); menu.Items.Add(options); menu.IsOpen = true;
    }
    private void Models_Click(object sender, RoutedEventArgs e)
    {
        ShowUnique(() => { var browser = new ModelWindow(_store, _viewModel.Downloads, _viewModel.ComputeChoices, _viewModel.Settings.Compute, _viewModel.EffectiveContext, _viewModel) { Owner = this }; browser.Closed += async (_, _) => await _viewModel.RefreshModelsAsync(browser.DownloadedModel); return browser; });
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void EnableComputer() { _viewModel.SelectedWorkMode = WorkModes.Find("computer"); InputBox.Focus(); }
    public void OpenSettings(int tab = 0) { var settings = ShowUnique(() => new SettingsWindow(_viewModel) { Owner = this }); settings.SelectTab(tab); }
    private static T ShowUnique<T>(Func<T> create) where T : Window
    {
        T? existing = Application.Current.Windows.OfType<T>().FirstOrDefault(w => w.IsVisible);
        if (existing is not null) { if (existing.WindowState == WindowState.Minimized) { existing.WindowState = WindowState.Normal; } existing.Activate(); return existing; }
        T window = create(); window.Show(); return window;
    }
    private void ProcessPopup_Opened(object? sender, EventArgs e) => ((ProcessListPanel)ProcessContent.Content).Refresh();
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
    private void Skills_Click(object sender, RoutedEventArgs e) => new SkillsWindow(_viewModel) { Owner = this }.Show();
    private void SkillPicker_Click(object sender, RoutedEventArgs e) => new SkillPickerWindow(_viewModel) { Owner = this }.Show();
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
        Item("新しいプロジェクト", () => new ProjectWindow(_viewModel) { Owner = this }.ShowDialog());
        if (_viewModel.SelectedProject is KnowledgeProject project) { Item("このプロジェクトを管理", () => new ProjectWindow(_viewModel, project) { Owner = this }.ShowDialog()); }
        Item("共通メモリを管理", () => OpenSettings(2));
        menu.IsOpen = true;
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
        Item("画像・文書を添付…", async () => { var picker = new OpenFileDialog { Filter = AttachmentService.Filter, Multiselect = true }; if (picker.ShowDialog(this) == true) { await _viewModel.AttachFilesAsync(picker.FileNames); } });
        if (menu.Items[0] is MenuItem attachment) { attachment.IsEnabled = _viewModel.CanChange; attachment.ToolTip = "実行中の推論・準備が終わると添付できます。別の依頼は送信・予約できます。"; ToolTipService.SetShowOnDisabled(attachment, true); }
        Item("画像認識モデルの設定…", () => OpenMedia("vision"));
        Item("Computer Use · チャットからPCを操作", EnableComputer);
        menu.Items.Add(new Separator());
        Item("画像を生成…", () => OpenMedia("image"));
        Item("ドキュメントを生成…", () => OpenMedia());
        Item("直前の回答を文書にする…", () => OpenMedia(initial: _viewModel.SelectedConversation?.Messages.LastOrDefault(m => m.Role == "assistant")?.Content));
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
    private void WorkOptions_Click(object sender, RoutedEventArgs e) => new WorkOptionsWindow(_viewModel) { Owner = this }.ShowDialog();
    private void WorkActivity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatMessage { Work: not null } answer }) { new WorkWindow(_viewModel, answer) { Owner = this }.Show(); }
    }
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
    private void Branch_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: ConversationBranch branch } && _viewModel is not null) { _viewModel.SelectedBranch = branch; }
    }
    private void Latest_Click(object sender, RoutedEventArgs e) => FollowOutput(true);
    private void DismissError_Click(object sender, RoutedEventArgs e) => _viewModel.Error = "";
    private void Mode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModePopup?.IsOpen == true) { ModePopup.IsOpen = false; ModeButton.Focus(); }
    }
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
        if (sender is not Button { DataContext: Conversation conversation } button) { return; }
        var menu = new ContextMenu { PlacementTarget = button };
        void Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("名前を変更", async () => { var dialog = new TextPromptWindow("会話の名前", conversation.Title) { Owner = this }; if (dialog.ShowDialog() == true) { await _viewModel.RenameConversationAsync(conversation, dialog.Result); } });
        Item(conversation.IsPinned ? "ピン留めを解除" : "ピン留め", async () => await _viewModel.TogglePinAsync(conversation));
        Item(conversation.IsArchived ? "保管から戻す" : "アーカイブ", async () => await _viewModel.ToggleArchiveAsync(conversation));
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
        void Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        menu.Items.Add(new MenuItem { Header = "モデルを読み込む", Command = _viewModel.PreloadCommand });
        menu.Items.Add(new MenuItem { Header = "モデルをメモリから解放", Command = _viewModel.UnloadCommand });
        Item("直前の回答をメモリに保存…", async () =>
        {
            ChatMessage? answer = _viewModel.SelectedConversation?.Messages.LastOrDefault(m => m.Role == "assistant" && m.Content.Length > 0);
            if (answer is null) { return; }
            var editor = new TextPromptWindow((_viewModel.SelectedProject?.Name ?? "共通") + "の保存メモリ（4,000文字まで）", answer.Content[..Math.Min(4000, answer.Content.Length)], true, 4000) { Owner = this };
            if (editor.ShowDialog() == true && !string.IsNullOrWhiteSpace(editor.Result))
            { try { await _viewModel.SaveMemoryAsync(editor.Result); } catch (Exception ex) { _viewModel.Error = ex.Message; } }
        });
        Item("この会話のカスタム指示", async () =>
        {
            if (_viewModel.SelectedConversation is not Conversation conversation) { return; }
            var dialog = new TextPromptWindow("この会話のカスタム指示", conversation.CustomInstruction, true) { Owner = this };
            if (dialog.ShowDialog() == true) { conversation.CustomInstruction = dialog.Result; await _viewModel.PersistSafelyAsync(); }
        });
        Item("会話の引き継ぎメモ", async () =>
        {
            if (!_viewModel.CanEditConversation) { _viewModel.Error = "この会話の推論と予約を終えてから引き継ぎメモを変更してください。"; return; }
            if (_viewModel.SelectedConversation is not Conversation conversation) { return; }
            var dialog = new TextPromptWindow("引き継ぎメモを確認・編集", conversation.Summary, true) { Owner = this };
            if (dialog.ShowDialog() == true) { conversation.Summary = dialog.Result; await _viewModel.PersistSafelyAsync(); }
        });
        Item("この会話をMarkdownで書き出す", async () =>
        {
            if (_viewModel.SelectedConversation is not Conversation conversation) { return; }
            var picker = new SaveFileDialog { Filter = "Markdown (*.md)|*.md", FileName = "Eluna-chat.md" };
            if (picker.ShowDialog(this) == true)
            {
                try { await File.WriteAllTextAsync(picker.FileName, "# " + conversation.Title + "\n\n" + string.Join("\n\n", conversation.Messages.Select(m => "## " + m.Speaker + "\n\n" + m.Content))); }
                catch (IOException) { _viewModel.Error = "会話を書き出せませんでした。保存先を確認してください。"; }
            }
        });
        menu.IsOpen = true;
    }
    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatMessage message }) { CopyMessage(message); }
    }
    private void CopyMessage(ChatMessage message)
    {
        try { Clipboard.SetText(message.Content); }
        catch (System.Runtime.InteropServices.COMException) { _viewModel.Error = "コピーできませんでした。もう一度試してください。"; }
    }
    private async void Regenerate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatMessage message }) { await _viewModel.RegenerateAsync(message); }
    }
    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatMessage message }) { await _viewModel.ContinueAsync(message); }
    }
    private async void EditMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChatMessage message }) { return; }
        var dialog = new TextPromptWindow("質問を編集して別の回答を生成", message.Content, true) { Owner = this };
        if (dialog.ShowDialog() == true) { await _viewModel.EditAndGenerateAsync(message, dialog.Result); }
    }
    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string text }) { _viewModel.Input = text; InputBox.Focus(); InputBox.CaretIndex = text.Length; }
    }
    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.Enter && !_composing && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && (!_viewModel.Settings.ControlEnterToSend || control))
        {
            if (_viewModel.SendCommand.CanExecute(null)) { e.Handled = true; _viewModel.SendCommand.Execute(null); }
        }
    }
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_composing) { return; }
        if (e.Key == Key.Escape)
        {
            if (ModePopup.IsOpen) { ModePopup.IsOpen = false; ModeButton.Focus(); e.Handled = true; return; }
            if (ModelSelector.IsDropDownOpen || ProjectSelector.IsDropDownOpen || BranchSelector.IsDropDownOpen || e.OriginalSource is ComboBox { IsDropDownOpen: true }) { return; }
            if (_viewModel.CanStopSelected) { _viewModel.StopCommand.Execute(null); e.Handled = true; }
            return;
        }
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || _composing) { return; }
        if (e.Key == Key.N) { _viewModel.NewChatCommand.Execute(null); InputBox.Focus(); e.Handled = true; }
        if (e.Key == Key.B) { _viewModel.ToggleSidebar(); e.Handled = true; }
        if (e.Key == Key.F) { if (!_viewModel.SidebarVisible) { _viewModel.ToggleSidebar(); } HistorySearchBox.Focus(); e.Handled = true; }
        if (e.Key == Key.End && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase) { FollowOutput(true); e.Handled = true; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ChatMessage? answer = _viewModel.SelectedConversation?.Messages.LastOrDefault(m => m.Role == "assistant");
            if (e.Key == Key.R && answer is not null && _viewModel.CanGenerate) { await _viewModel.RegenerateAsync(answer); e.Handled = true; }
            if (e.Key == Key.C && answer is not null) { CopyMessage(answer); e.Handled = true; }
        }
    }
    private void MessagesScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _messagesScroll) || _messagesScroll is null) { return; }
        if (e.ExtentHeightChange == 0) { _followOutput = _messagesScroll.ScrollableHeight - _messagesScroll.VerticalOffset < 80; }
        LatestButton.Visibility = _followOutput ? Visibility.Collapsed : Visibility.Visible;
        _readingSave.Stop(); _readingSave.Start();
    }
    private void RememberReadingPosition() { if (_scrollKey is not null && _messagesScroll is not null) { _readingPositions.Set(_scrollKey, _messagesScroll.VerticalOffset, _followOutput); } }
    private void FollowOutput(bool force)
    {
        string key = $"{_viewModel.SelectedConversation?.Id}/{_viewModel.SelectedBranch?.Id}";
        if (_scrollKey != key)
        {
            _scrollKey = key;
            if (_viewModel.HistorySearch.Trim().Length == 0 && _readingPositions.Find(key) is { } position)
            {
                _followOutput = position.Follow;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { _messagesScroll?.ScrollToVerticalOffset(position.Offset); LatestButton.Visibility = _followOutput ? Visibility.Collapsed : Visibility.Visible; }); return;
            }
            _followOutput = true;
        }
        _followOutput |= force;
        if (_viewModel.HistorySearch.Trim().Length > 0 && _viewModel.SelectedConversation?.Messages.FirstOrDefault(m => m.Content.Contains(_viewModel.HistorySearch.Trim(), StringComparison.OrdinalIgnoreCase)) is ChatMessage match)
        { _followOutput = false; Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { MessagesList.ScrollIntoView(match); LatestButton.Visibility = Visibility.Visible; }); return; }
        if (_followOutput)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                _messagesScroll ??= FindChild<ScrollViewer>(MessagesList);
                if (_viewModel.SelectedConversation?.Messages.LastOrDefault() is ChatMessage last) { MessagesList.ScrollIntoView(last); }
                _messagesScroll?.ScrollToEnd(); LatestButton.Visibility = Visibility.Collapsed;
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
        _shutdownStarted = true; IsEnabled = false;
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
        _closing = true; Close();
    }
}
