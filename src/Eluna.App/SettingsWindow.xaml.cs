using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public partial class SettingsWindow : Window
{
    private readonly ChatViewModel _viewModel;
    private readonly Conversation? _room;
    private string _channelBaseline = "";
    private bool _fieldsReady;
    private string _baseline = "", _memoryBaseline = "";
    private bool _saving;
    public void SelectTab(int tab) => SettingsTabs.SelectedIndex = Math.Clamp(tab, 0, SettingsTabs.Items.Count - 1);
    public SettingsWindow(ChatViewModel viewModel, int initialTab = 0)
    {
        InitializeComponent(); _viewModel = viewModel; _room = viewModel.SelectedConversation; DataContext = viewModel; Appearance.Attach(this);
        ComputeBox.ItemsSource = viewModel.ComputeChoices;
        ComputeBox.SelectedItem = viewModel.ComputeChoices.FirstOrDefault(d => d.Id == viewModel.Settings.Compute) ?? viewModel.ComputeChoices.FirstOrDefault();
        ContextBox.ItemsSource = new[] { 2048, 4096, 8192, 16384 }; ContextBox.SelectedItem = viewModel.Settings.ContextSize;
        ConcurrencyBox.ItemsSource = new[] { 1, 2, 3, 4 }; ConcurrencyBox.SelectedItem = viewModel.Settings.MaxConcurrentInferences;
        DownloadConcurrencyBox.ItemsSource = new[] { 1, 2, 3, 4 }; DownloadConcurrencyBox.SelectedItem = viewModel.Settings.MaxConcurrentDownloads;
        MediaConcurrencyBox.ItemsSource = new[] { 1, 2, 3, 4 }; MediaConcurrencyBox.SelectedItem = viewModel.Settings.MaxConcurrentMedia;
        ThemeBox.ItemsSource = new[] { new KeyValuePair<string, string>("dark", "ダーク"), new("light", "ライト") };
        ThemeBox.SelectedIndex = viewModel.Settings.Theme == "light" ? 1 : 0;
        FontBox.ItemsSource = new[] { 12.0, 14, 15, 16, 18, 20, 24, 28, viewModel.Settings.ChatFontSize }.Distinct().Order().ToArray(); FontBox.SelectedItem = viewModel.Settings.ChatFontSize;
        LineBox.ItemsSource = new[] { 18.0, 22, 25, 28, 32, 36, 42, 50, 60, viewModel.Settings.LineHeight }.Distinct().Order().ToArray(); LineBox.SelectedItem = viewModel.Settings.LineHeight;
        ControlEnterBox.IsChecked = viewModel.Settings.ControlEnterToSend;
        ComputerAwakeBox.IsChecked = viewModel.Settings.ComputerUseKeepAwake;
        InstructionBox.Text = viewModel.Settings.SystemInstruction;
        UiScaleBox.ItemsSource = new[] { new KeyValuePair<double, string>(1, "100%"), new(1.15, "115%"), new(1.3, "130%"), new(1.5, "150%") }; UiScaleBox.SelectedValue = viewModel.Settings.UiScale;
        CommonMemoryEditor.LoadNotes(viewModel.CommonMemories); SettingsTabs.SelectedIndex = initialTab;
        WorkModeBox.ItemsSource = viewModel.WorkChoices; WorkModeBox.SelectedItem = viewModel.SelectedWorkMode;
        ThinkingBox.ItemsSource = viewModel.ModeChoices; ThinkingBox.SelectedItem = viewModel.SelectedMode;
        SettingRecipientBox.ItemsSource = viewModel.Recipients.Where(a => a.Id is not null).ToArray(); SettingRecipientBox.SelectedItem = viewModel.SelectedRecipient ?? viewModel.Recipients.FirstOrDefault();
        ChannelInstructionBox.Text = _room?.CustomInstruction ?? ""; ChannelScopeText.Text = (_room?.NavigationLabel ?? "会話未選択") + "\n" + viewModel.ProjectScopeHint;
        BranchSelector.ItemsSource = _room?.Branches; BranchSelector.SelectedItem = viewModel.SelectedBranch;
        ProcessContent.Content = new Eluna.App.Controls.ProcessListPanel(viewModel); DiscussionPanel.Chat = viewModel; DiscussionPanel.Work = _room?.Messages.LastOrDefault(m => m.Work is not null)?.Work;
        if (_room?.Messages.LastOrDefault(m => m.Role == "assistant") is { } last) { ShowMessage(last); }
        _channelBaseline = ChannelSnapshot(); _fieldsReady = true;
        _baseline = Snapshot(); _memoryBaseline = JsonSerializer.Serialize(CommonMemoryEditor.GetNotes());
        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Edited()));
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => Edited()));
        ControlEnterBox.Click += (_, _) => Edited(); CommonMemoryEditor.Changed += (_, _) => Edited();
        ComputerAwakeBox.Click += (_, _) => Edited();
        Closing += ReviewChildren;
        UnsavedChanges.Protect(this, () => Snapshot() != _baseline, SaveAsync);
        Closed += (_, _) => LicenseView.Dispose();
    }
    private string ChannelSnapshot() => JsonSerializer.Serialize(new { Work = (WorkModeBox.SelectedItem as WorkMode)?.Id, Thinking = (ThinkingBox.SelectedItem as InferenceMode)?.Id, Recipient = (SettingRecipientBox.SelectedItem as AgentChoice)?.Id, ChannelInstructionBox.Text });
    private string Snapshot() => JsonSerializer.Serialize(new { ComputeBox.SelectedItem, Context = ContextBox.SelectedItem, Concurrency = ConcurrencyBox.SelectedItem, DownloadConcurrency = DownloadConcurrencyBox.SelectedItem, MediaConcurrency = MediaConcurrencyBox.SelectedItem, Font = FontBox.SelectedItem, Line = LineBox.SelectedItem, Theme = ThemeBox.SelectedIndex, UiScale = UiScaleBox.SelectedValue, ControlEnterBox.IsChecked, ComputerUseKeepAwake = ComputerAwakeBox.IsChecked, InstructionBox.Text, Memories = CommonMemoryEditor.GetNotes(), Channel = ChannelSnapshot() });
    private void Edited() { bool dirty = Snapshot() != _baseline; SaveStateText.Text = dirty ? "未保存の変更があります" : "保存済み"; Title = dirty ? "設定 · 変更あり · Eluna" : "設定 · Eluna"; }
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();
    private async Task<bool> SaveAsync()
    {
        if (_saving) { return false; }
        if (ComputeBox.SelectedItem is not ComputeDevice device || ContextBox.SelectedItem is not int context ||
            ConcurrencyBox.SelectedItem is not int concurrency || DownloadConcurrencyBox.SelectedItem is not int downloadConcurrency || MediaConcurrencyBox.SelectedItem is not int mediaConcurrency || FontBox.SelectedItem is not double font || LineBox.SelectedItem is not double line || ThemeBox.SelectedItem is not KeyValuePair<string, string> theme || UiScaleBox.SelectedValue is not double scale) { return false; }
        _saving = true; SaveButton.IsEnabled = false; string savedSnapshot = Snapshot(), savedChannel = ChannelSnapshot(), savedMemories = JsonSerializer.Serialize(CommonMemoryEditor.GetNotes());
        try
        {
            bool channelChanged = savedChannel != _channelBaseline;
            if (channelChanged && !CheckRoom()) { return false; }
            if (channelChanged && SettingRecipientBox.SelectedItem is AgentChoice { Id: Guid recipient } choice)
            {
                if (_room?.Room?.Members.Contains(recipient) != true) { throw new InvalidOperationException("参加者が変更されています。設定を開き直してください。"); }
                _viewModel.SelectedRecipient = choice;
            }
            if (channelChanged && _room is not null) { _room.CustomInstruction = ChannelInstructionBox.Text; }
            await _viewModel.ApplySettingsAsync(_viewModel.Settings with
            {
                Compute = device.Id,
                ContextSize = context,
                MaxConcurrentInferences = concurrency,
                MaxConcurrentDownloads = downloadConcurrency,
                MaxConcurrentMedia = mediaConcurrency,
                ChatFontSize = font,
                LineHeight = line,
                Theme = theme.Key,
                UiScale = scale,
                ControlEnterToSend = ControlEnterBox.IsChecked == true,
                ComputerUseKeepAwake = ComputerAwakeBox.IsChecked == true,
                SystemInstruction = InstructionBox.Text,
                WorkMode = channelChanged ? (WorkModeBox.SelectedItem as WorkMode)?.Id ?? _viewModel.Settings.WorkMode : _viewModel.Settings.WorkMode,
                InferenceMode = channelChanged ? (ThinkingBox.SelectedItem as InferenceMode)?.Id ?? _viewModel.Settings.InferenceMode : _viewModel.Settings.InferenceMode
            }, JsonSerializer.Serialize(CommonMemoryEditor.GetNotes()) == _memoryBaseline ? null : CommonMemoryEditor.GetNotes());
            _baseline = savedSnapshot; _channelBaseline = savedChannel; _memoryBaseline = savedMemories; Edited(); return true;
        }
        catch (Exception ex) { SaveStateText.Text = "保存できませんでした: " + ex.Message; return false; }
        finally { _saving = false; SaveButton.IsEnabled = true; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private MainWindow? Main => Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
    private void Models_Click(object sender, RoutedEventArgs e) => Main?.OpenModels();
    private void ManageModels_Click(object sender, RoutedEventArgs e) => Main?.OpenModelManager();
    private void Downloads_Click(object sender, RoutedEventArgs e) => Main?.OpenDownloads();
    private void Data_Click(object sender, RoutedEventArgs e) => Main?.OpenData();
    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_viewModel.DataDirectory) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存場所を開けませんでした"); }
    }
    private void Updates_Click(object sender, RoutedEventArgs e) => new UpdateWindow { Owner = Main ?? (Window)this }.Show();
    private void Skills_Click(object sender, RoutedEventArgs e) => new SkillsWindow(_viewModel) { Owner = Main ?? (Window)this }.Show();
}
