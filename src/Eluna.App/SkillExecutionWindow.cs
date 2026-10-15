using System.Windows;
using System.Windows.Controls;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App;

public sealed class SkillExecutionWindow : Window
{
    public bool Accepted { get; private set; }
    public SkillExecutionWindow(SkillExecutionRequest request)
    {
        Title = "スキルの補助コードを確認 · Eluna"; Width = 900; Height = 760; MinWidth = 620; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = request.SkillName + " · " + request.ScriptPath, FontSize = 21, TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(new TextBlock { Text = $"依頼ID: {request.JobId}\n会話: {request.ConversationTitle}\n範囲: {request.ProjectLabel} · モデル: {request.ModelLabel}\nスキル版: {request.Revision}", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0), FontSize = 12 });
        heading.Children.Add(new TextBlock { Text = "以下のPowerShellコードをPCのユーザー権限で実行します。ファイル・ネットワークへアクセスでき、OSの隔離環境ではありません。実行上限は60秒です。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 16) });
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var deny = new Button { Content = "実行しない", Margin = new(0, 0, 8, 0) }; deny.Click += (_, _) => Close(); footer.Children.Add(deny);
        var allow = new Button { Content = "許可して実行" }; allow.SetResourceReference(StyleProperty, "PrimaryButton"); allow.Click += (_, _) => { Accepted = true; Close(); }; footer.Children.Add(allow);
        var tabs = new TabControl { BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent };
        void Tab(string title, string text) { tabs.Items.Add(new TabItem { Header = title, Content = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, VerticalContentAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas, Yu Gothic UI") }, Style = (Style)FindResource("WorkOptionsTab") }); }
        Tab("実行コード", request.Script); Tab("入力と保存先", "入力JSON:\n" + request.Input + "\n\n作成ファイルの保存先:\n" + request.OutputDirectory + "\n\nSKILLの資料・コードを専用の作業フォルダーへコピーし、-InputJson <JSONファイル> -OutputDirectory <保存先> を渡します。");
        System.Windows.Automation.AutomationProperties.SetName(tabs, "実行コードと入力・保存先"); root.Children.Add(tabs);
    }
}
