using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;
using Microsoft.Win32;

namespace Eluna.App;

public sealed class ModelsWindow : Window
{
    private readonly ChatViewModel _viewModel; private readonly ModelsService _models;
    private LocalModel? _selected; private CancellationTokenSource? _inspect; private readonly CancellationTokenSource _lifetime = new();
    private bool _changing;
    public ModelsWindow(AppStore store, ChatViewModel viewModel)
    {
        _viewModel = viewModel; _models = new(store); Title = "モデル管理 · Eluna"; Width = 1080; Height = 820; MinWidth = 820; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new Grid { Margin = new(24) }; root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var head = new StackPanel { Margin = new(0, 0, 0, 18) }; head.Children.Add(new TextBlock { Text = "モデル管理", FontSize = 24, FontWeight = FontWeights.SemiBold });
        var location = new TextBlock { Text = store.ModelsDirectory, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) }; head.Children.Add(location);
        var add = new Button { Content = "モデルを追加", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 8) }; add.Click += (_, _) => (Application.Current.MainWindow as MainWindow)?.OpenModels(); head.Children.Add(add);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; head.Children.Add(status); root.Children.Add(head);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new(300) }); body.ColumnDefinitions.Add(new()); Grid.SetRow(body, 1); root.Children.Add(body);
        var left = new DockPanel { Margin = new(0, 0, 20, 0) }; body.Children.Add(left);
        var searchLabel = new TextBlock { Text = "モデル名・配布元で検索", FontSize = 11, Margin = new(0, 0, 0, 5) }; DockPanel.SetDock(searchLabel, Dock.Top); left.Children.Add(searchLabel);
        var search = new TextBox { Margin = new(0, 0, 0, 12), ToolTip = "モデル名・配布元で検索" }; AutomationProperties.SetName(search, "モデル名・配布元で検索"); DockPanel.SetDock(search, Dock.Top); left.Children.Add(search);
        var view = new ListCollectionView(viewModel.Models); view.Filter = item => item is LocalModel m && (search.Text.Length == 0 || m.DisplayName.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || m.Repository.Contains(search.Text, StringComparison.OrdinalIgnoreCase));
        search.TextChanged += (_, _) => view.Refresh();
        var list = new ListBox { ItemsSource = view, ItemTemplate = WorkWindow.CardTemplate("DisplayName", "SizeLabel") }; left.Children.Add(list);
        var detail = new StackPanel { Margin = new(0, 0, 12, 0) }; var scroll = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) }; detail.Children.Add(info);
        var name = new TextBox(); AutomationProperties.SetName(name, "モデルの表示名"); detail.Children.Add(new TextBlock { Text = "名前", Margin = new(0, 0, 0, 6) }); detail.Children.Add(name);
        var favorite = new CheckBox { Content = "お気に入り（モデル一覧の先頭に表示）", Margin = new(0, 14, 0, 18) }; detail.Children.Add(favorite);
        detail.Children.Add(new TextBlock { Text = "このモデルの推論設定", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 12) });
        ComboBox Choice(string label, object[] choices) { detail.Children.Add(new TextBlock { Text = label, Margin = new(0, 8, 0, 6) }); var box = new ComboBox { ItemsSource = choices }; AutomationProperties.SetName(box, label); detail.Children.Add(box); return box; }
        ComboBox context = Choice("記憶長（0 = 全体設定を使用）", [0, 2048, 4096, 8192, 16384, 32768]);
        ComboBox mode = Choice("選択した時の思考量", InferenceModes.All.Cast<object>().ToArray()); mode.ItemTemplate = (DataTemplate)FindResource("WorkNameTemplate");
        TextBox Field(string label) { detail.Children.Add(new TextBlock { Text = label, Margin = new(0, 8, 0, 6) }); var input = new TextBox(); AutomationProperties.SetName(input, label); detail.Children.Add(input); return input; }
        TextBox temperature = Field("温度（0〜2）"), topP = Field("Top P（0より大きく1以下）"), topK = Field("Top K（0〜1000）"), repeat = Field("繰り返しの抑制（0.5〜2）"), output = Field("回答の最大token数（128〜8192）");
        var presetPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 16, 0, 0) }; detail.Children.Add(presetPanel);
        void Fill(ModelPreferences p) { context.SelectedItem = p.ContextSize ?? 0; mode.SelectedItem = InferenceModes.Find(p.DefaultMode); temperature.Text = p.Temperature.ToString(CultureInfo.InvariantCulture); topP.Text = p.TopP.ToString(CultureInfo.InvariantCulture); topK.Text = p.TopK.ToString(CultureInfo.InvariantCulture); repeat.Text = p.RepeatPenalty.ToString(CultureInfo.InvariantCulture); output.Text = p.MaxOutputTokens.ToString(CultureInfo.InvariantCulture); }
        foreach (var preset in new[] { ("標準", new ModelPreferences()), ("安定", new ModelPreferences(Temperature: 0.2, TopP: 0.9)), ("創作", new ModelPreferences(Temperature: 0.9, TopP: 0.98, RepeatPenalty: 1.05)) })
        { var button = new Button { Content = preset.Item1, Margin = new(0, 0, 8, 0) }; button.Click += (_, _) => Fill(preset.Item2 with { DefaultMode = _selected?.Profile?.SupportsThinking == false ? "fast" : "standard" }); presetPanel.Children.Add(button); }
        var footer = new WrapPanel { Margin = new(0, 18, 0, 0) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var save = new Button { Content = "名前・設定を保存", IsEnabled = false }; save.SetResourceReference(StyleProperty, "PrimaryButton"); footer.Children.Add(save);
        var card = new Button { Content = "能力・モデルカード", IsEnabled = false, Margin = new(8, 0, 0, 0) }; footer.Children.Add(card);
        var remove = new Button { Content = "削除", IsEnabled = false, Margin = new(8, 0, 0, 0) }; footer.Children.Add(remove);
        var move = new Button { Content = "保存先を変更・移動", Margin = new(8, 0, 0, 0) }; footer.Children.Add(move);
        void Buttons() { save.IsEnabled = remove.IsEnabled = _selected is not null && !_changing && viewModel.CanChange; card.IsEnabled = _selected is not null; move.IsEnabled = !_changing && viewModel.CanChange; detail.IsEnabled = !_changing && viewModel.CanChange; }
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { Buttons(); if (!viewModel.CanChange) { status.Text = "推論・予約中はモデルの変更を保留しています。編集中の内容は保持します。"; } }
        viewModel.PropertyChanged += Changed;
        string baseline = ""; bool loadingDraft = false, selecting = false, saving = false;
        string Draft() => JsonSerializer.Serialize(new { name.Text, favorite.IsChecked, Context = context.SelectedItem, Mode = mode.SelectedItem, Temperature = temperature.Text, TopP = topP.Text, TopK = topK.Text, Repeat = repeat.Text, Output = output.Text });
        bool Dirty() => _selected is not null && baseline.Length > 0 && Draft() != baseline;
        void MarkDirty() { if (!loadingDraft) { Title = Dirty() ? "モデル管理 · 変更あり · Eluna" : "モデル管理 · Eluna"; } }
        detail.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => MarkDirty()));
        detail.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => MarkDirty())); favorite.Click += (_, _) => MarkDirty();
        UnsavedChanges.Protect(this, Dirty, SaveAsync);
        string? selectedId = viewModel.SelectedModel?.Id;
        async Task SelectAsync(LocalModel? model)
        {
            if (model is null || selecting) { return; }
            if (!saving && !loadingDraft && _selected?.Id != model.Id && Dirty())
            {
                bool? choice = UnsavedChanges.Choose(this);
                if (choice is null || choice == true && !await SaveAsync()) { selecting = true; list.SelectedItem = _selected; selecting = false; return; }
            }
            loadingDraft = true;
            selectedId = model.Id;
            _inspect?.Cancel(); _inspect?.Dispose(); _inspect = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); CancellationToken token = _inspect.Token;
            _selected = model; name.Text = model.DisplayName; favorite.IsChecked = model.IsFavorite;
            mode.ItemsSource = model.Profile?.SupportsThinking == false ? InferenceModes.All.Where(m => m.Id == "fast").Cast<object>().ToArray() : InferenceModes.All.Cast<object>().ToArray();
            Fill(model.Options with { DefaultMode = model.Profile?.SupportsThinking == false ? "fast" : model.Options.DefaultMode }); baseline = Draft(); loadingDraft = false; MarkDirty(); Buttons();
            info.Text = ModelInfoWindow.Summary(model, model.Options.ContextSize ?? viewModel.Settings.ContextSize, HardwareAdvisor.Capture(viewModel.ComputeChoices, viewModel.Settings.Compute));
            if (model.Profile is null)
            {
                try { LocalModel inspected = await _models.InspectAsync(model, token); if (!token.IsCancellationRequested && _selected?.Id == model.Id) { _selected = inspected; if (inspected.Profile?.SupportsThinking == false) { mode.ItemsSource = InferenceModes.All.Where(m => m.Id == "fast").Cast<object>().ToArray(); mode.SelectedItem = InferenceModes.Find("fast"); } info.Text = ModelInfoWindow.Summary(inspected, inspected.Options.ContextSize ?? viewModel.Settings.ContextSize, HardwareAdvisor.Capture(viewModel.ComputeChoices, viewModel.Settings.Compute)); } }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or OverflowException or System.Text.DecoderFallbackException) { if (!token.IsCancellationRequested) { status.Text = "能力情報を読み取れませんでした。モデルの選択・利用は試せます。"; } }
            }
        }
        list.SelectionChanged += async (_, _) => await SelectAsync(list.SelectedItem as LocalModel);
        Loaded += async (_, _) => await SelectAsync(list.SelectedItem as LocalModel ?? viewModel.SelectedModel ?? viewModel.Models.FirstOrDefault());
        void ModelsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(() => { if (!_lifetime.IsCancellationRequested && list.SelectedItem is null) { list.SelectedItem = viewModel.Models.FirstOrDefault(m => m.Id == selectedId) ?? viewModel.SelectedModel ?? viewModel.Models.FirstOrDefault(); } });
        }
        viewModel.Models.CollectionChanged += ModelsChanged;
        async Task<bool> SaveAsync()
        {
            if (_selected is not LocalModel selected || !viewModel.CanChange) { return false; }
            if (!double.TryParse(temperature.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double t) || !double.TryParse(topP.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double p) ||
                !int.TryParse(topK.Text, out int k) || !double.TryParse(repeat.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) || !int.TryParse(output.Text, out int max) || context.SelectedItem is not int c || mode.SelectedItem is not InferenceMode thought || string.IsNullOrWhiteSpace(name.Text))
            { status.Text = "名前と数値を確認してください。小数点には . を使います。"; return false; }
            saving = true;
            try { var settings = new ModelPreferences(c == 0 ? null : c, t, p, k, r, max, thought.Id); ModelPreferences.Validate(settings); await viewModel.ManageModelsAsync(token => _models.UpdateAsync(selected with { DisplayName = name.Text.Trim(), IsFavorite = favorite.IsChecked == true, Preferences = settings }, token)); status.Text = "モデル設定を保存しました。次の生成から反映します。"; baseline = Draft(); MarkDirty(); return true; }
            catch (OperationCanceledException) { status.Text = "設定の保存を停止しました。"; }
            catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException) { status.Text = ex.Message; }
            finally { saving = false; }
            return false;
        }
        save.Click += async (_, _) => await SaveAsync();
        card.Click += (_, _) => { if (_selected is LocalModel model) { new ModelInfoWindow(store, model.Repository, model.Revision, model.DisplayName, info.Text) { Owner = this }.Show(); } };
        remove.Click += async (_, _) =>
        {
            if (_selected is not LocalModel model || !viewModel.CanChange || MessageBox.Show(this, $"「{model.DisplayName}」の{model.Files.Count}ファイル（{model.SizeLabel}）を削除しますか？取得キューは一時停止します。", "モデルを削除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { return; }
            try { await viewModel.ManageModelsAsync(async token => { await viewModel.Downloads.PauseAllAsync(); viewModel.ReleaseModel(); await _models.DeleteAsync(model, token); viewModel.Downloads.MarkDeleted(model.Id); }); status.Text = "モデルを削除しました。"; }
            catch (OperationCanceledException) { status.Text = "削除を停止しました。"; }
            catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException) { status.Text = ex.Message; }
        };
        move.Click += async (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = "モデルの移動先（専用フォルダ）を選択", InitialDirectory = store.ModelsDirectory };
            if (!viewModel.CanChange || picker.ShowDialog(this) != true) { return; }
            _changing = true; Buttons(); list.IsEnabled = false;
            try { await viewModel.ManageModelsAsync(async token => { await viewModel.Downloads.PauseAllAsync(); viewModel.ReleaseModel(); using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token); await _models.MoveDirectoryAsync(picker.FolderName, new Progress<DownloadProgress>(p => status.Text = p.Label), linked.Token); }); location.Text = store.ModelsDirectory; status.Text = "保存先を変更しました。停止した取得はキューから再開できます。"; }
            catch (OperationCanceledException) { status.Text = "移動を停止しました。元のモデルは保管しています。"; }
            catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException) { status.Text = ex.Message; }
            finally { _changing = false; list.IsEnabled = true; Buttons(); }
        };
        list.SelectedItem = viewModel.SelectedModel ?? viewModel.Models.FirstOrDefault();
        status.Text = $"{viewModel.Models.Count}モデル · 使用量 {FileSize.Format(viewModel.Models.Sum(m => m.Size))}（途中ファイルは取得キューで確認）";
        Loaded += async (_, _) => { try { long usage = await _models.UsageAsync(_lifetime.Token); if (!_lifetime.IsCancellationRequested) { status.Text = $"{viewModel.Models.Count}モデル · 管理フォルダの使用量 {FileSize.Format(usage)}（途中ファイルを含む）"; } } catch (OperationCanceledException) { } catch (System.IO.IOException) { status.Text = "保存先の使用量を取得できませんでした。"; } };
        var capacityRefresh = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => { if (_selected is LocalModel model) { info.Text = ModelInfoWindow.Summary(model, model.Options.ContextSize ?? viewModel.Settings.ContextSize, HardwareAdvisor.Capture(viewModel.ComputeChoices, viewModel.Settings.Compute)); } }, Dispatcher); capacityRefresh.Stop(); Loaded += (_, _) => capacityRefresh.Start();
        Closed += (_, _) => { capacityRefresh.Stop(); _lifetime.Cancel(); _inspect?.Cancel(); _inspect?.Dispose(); _lifetime.Dispose(); viewModel.PropertyChanged -= Changed; viewModel.Models.CollectionChanged -= ModelsChanged; }; Content = root; Buttons();
    }
}
