using System.Windows;
using System.Windows.Controls;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class WorkOptionsWindow : Window
{
    private readonly TextBox _searches, _pages, _parallel, _seconds, _tokens, _rounds;
    private readonly ComboBox _style = new() { DisplayMemberPath = "Value", SelectedValuePath = "Key" }, _debateSearch = new() { DisplayMemberPath = "Value", SelectedValuePath = "Key" };
    private readonly CheckBox _automatic = new() { Content = "自由形ディベートを自動で進める" };
    private readonly TextBox _turns;
    internal DebateParticipantsEditor ParticipantsEditor { get; }
    internal TabControl OptionsTabs { get; }
    public WorkOptionsWindow(ChatViewModel viewModel)
    {
        Title = "検索・調査・議論の設定 · Eluna"; Width = 1040; Height = 820; MinWidth = 820; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        WorkOptions current = viewModel.Settings.WorkOptions ?? new();
        var root = new DockPanel { Margin = new(24) }; var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        ParticipantsEditor = new(current.Participants, viewModel.Models);
        OptionsTabs = new TabControl { Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new(0), Padding = new(0) }; root.Children.Add(OptionsTabs);
        var participantsTab = new TabItem { Header = "参加者", Content = ParticipantsEditor }; participantsTab.SetResourceReference(StyleProperty, "WorkOptionsTab"); OptionsTabs.Items.Add(participantsTab);
        var panel = new StackPanel(); var limitsTab = new TabItem { Header = "作業の上限", Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }; limitsTab.SetResourceReference(StyleProperty, "WorkOptionsTab"); OptionsTabs.Items.Add(limitsTab);
        OptionsTabs.SelectedIndex = viewModel.SelectedWorkMode.Id == "debate" ? 0 : 1;
        panel.Children.Add(new TextBlock { Text = "作業の上限", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "検索語を外部サービスへ送信します。本文が取れない資料は根拠として扱いません。設定した時間・回数・生成量で作業を止めます。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) });
        TextBox Field(string label, int value) { panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 8, 0, 6) }); var input = new TextBox { Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 180, HorizontalAlignment = HorizontalAlignment.Left }; System.Windows.Automation.AutomationProperties.SetName(input, label); panel.Children.Add(input); return input; }
        _searches = Field("検索語の回数（1〜20、各回で最大3つの検索先）", current.SearchLimit); _pages = Field("本文の取得件数（1〜30）", current.PageLimit); _parallel = Field("同時に取得するページ（1〜3）", current.ParallelPages); _seconds = Field("作業時間・秒（30〜3600）", current.SecondsLimit); _tokens = Field("全工程の生成量・token（2000〜40000）", current.TokenLimit); _rounds = Field("議論のラウンド数（1〜5）", current.Rounds);
        panel.Children.Insert(1, new TextBlock { Text = "議論の進め方", Margin = new(0, 8, 0, 6) });
        _style.ItemsSource = new[] { new KeyValuePair<string, string>("rounds", "ラウンド制"), new("free", "自由形式") }; _style.SelectedValue = current.DebateStyle; panel.Children.Insert(2, _style);
        _turns = Field("自由形式のAI発言上限（2〜60）", current.MaxTurns); _automatic.IsChecked = current.AutomaticDebate; _automatic.Margin = new(0, 12, 0, 12); panel.Children.Add(_automatic);
        panel.Children.Add(new TextBlock { Text = "自由形式のWeb検索", Margin = new(0, 8, 0, 6) });
        _debateSearch.ItemsSource = new[] { new KeyValuePair<string, string>("auto", "必要に応じて検索"), new("always", "必ず検索"), new("off", "検索しない") }; _debateSearch.SelectedValue = current.DebateSearch; panel.Children.Add(_debateSearch);
        panel.Children.Add(new TextBlock { Text = "チャンネルにAIが2名以上参加している場合は、そのメンバーで議論します。参加者の追加・モデル変更はチャンネル設定とエージェント管理から行えます。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 12, 0, 0) });
        var save = new Button { Content = "保存", Margin = new(8, 0, 0, 0) }; save.SetResourceReference(StyleProperty, "PrimaryButton");
        save.Click += async (_, _) =>
        {
            try
            {
                WorkOptions options = CreateOptions(); save.IsEnabled = false; await viewModel.ApplySettingsAsync(viewModel.Settings with { WorkOptions = options }); DialogResult = true;
            }
            catch (System.IO.InvalidDataException ex) { MessageBox.Show(this, ex.Message); }
            finally { save.IsEnabled = true; }
        };
        var cancel = new Button { Content = "キャンセル", IsCancel = true }; footer.Children.Add(cancel); footer.Children.Add(save); Content = root;
    }
    internal WorkOptions CreateOptions()
    {
        if (!int.TryParse(_searches.Text, out int searches) || !int.TryParse(_pages.Text, out int pages) || !int.TryParse(_parallel.Text, out int parallel) || !int.TryParse(_seconds.Text, out int seconds) || !int.TryParse(_tokens.Text, out int tokens) || !int.TryParse(_rounds.Text, out int rounds))
        { throw new System.IO.InvalidDataException("上限には整数を入力してください。"); }
        if (!int.TryParse(_turns.Text, out int turns)) { throw new System.IO.InvalidDataException("発言上限は整数にしてください。"); }
        var options = new WorkOptions(searches, pages, parallel, seconds, tokens, rounds, ParticipantsEditor.GetRoles(), (string)_style.SelectedValue, _automatic.IsChecked == true, turns, (string)_debateSearch.SelectedValue); ResearchRunner.ValidateOptions(options); return options;
    }
}

public sealed class WorkPlanWindow : Window
{
    public bool Accepted { get; private set; }
    public WorkPlanWindow(WorkSession work, bool modeless = false)
    {
        Title = "調査計画を確認 · Eluna"; Width = 760; Height = 690; MinWidth = 600; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(24) };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "調査の進め方を確認", FontSize = 22, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = work.Question, TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 16), MaxHeight = 100 });
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var panel = new Grid(); panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new()); panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new() { Height = new(150) }); root.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text = "論点・確認する内容（編集できます）", Margin = new(0, 0, 0, 8) });
        var plan = new TextBox { Text = work.Plan, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; System.Windows.Automation.AutomationProperties.SetName(plan, "調査計画"); Grid.SetRow(plan, 1); panel.Children.Add(plan);
        var label = new TextBlock { Text = "外部へ送信する検索語（1行に1つ）", Margin = new(0, 14, 0, 8) }; Grid.SetRow(label, 2); panel.Children.Add(label);
        var queries = new TextBox { Text = string.Join('\n', work.Queries), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; System.Windows.Automation.AutomationProperties.SetName(queries, "外部へ送信する検索語"); Grid.SetRow(queries, 3); panel.Children.Add(queries);
        var cancel = new Button { Content = "今は停止", IsCancel = true }; cancel.Click += (_, _) => Close(); footer.Children.Add(cancel);
        var start = new Button { Content = "この計画で開始", Margin = new(8, 0, 0, 0) }; start.SetResourceReference(StyleProperty, "PrimaryButton");
        start.Click += (_, _) =>
        {
            List<string> lines = ResearchRunner.ParseQueries(queries.Text, work.Options.SearchLimit);
            if ((lines.Count == 0 && work.WebEnabled != false) || string.IsNullOrWhiteSpace(plan.Text)) { MessageBox.Show(this, "論点と検索語を入力してください。"); return; }
            work.Plan = plan.Text; work.Queries.Clear(); work.Queries.AddRange(lines); Accepted = true; if (modeless) { Close(); } else { DialogResult = true; }
        }; footer.Children.Add(start); Content = root;
    }
}
