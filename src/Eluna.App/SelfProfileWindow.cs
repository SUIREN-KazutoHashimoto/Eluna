using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Eluna.App.Controls;
using Eluna.App.Models;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

public sealed class SelfProfileWindow : Window
{
    internal TextBox NameBox { get; } = new() { MaxLength = 80 };
    internal TextBox IconBox { get; } = new() { MaxLength = 16 };
    private readonly TextBox _about = new() { MaxLength = 2000, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 100, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    public SelfProfileWindow(ChatViewModel chat)
    {
        Title = "自分のプロフィール · Eluna"; Width = 500; Height = 550; MinWidth = 370; MinHeight = 380; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        var root = new DockPanel { Margin = new(24) }; Content = root; var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); footer.Children.Add(status); var save = new Button { Content = "プロフィールを保存", HorizontalAlignment = HorizontalAlignment.Right }; save.SetResourceReference(StyleProperty, "PrimaryButton"); footer.Children.Add(save);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock { Text = "自分のプロフィール", FontSize = 23 }); body.Children.Add(new TextBlock { Text = "ここでの名前とアイコンを設定します。他のメンバーと同じように、発言とメンバー一覧に表示します。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) });
        void Field(string label, TextBox box) { body.Children.Add(new TextBlock { Text = label, Margin = new(0, 12, 0, 6) }); body.Children.Add(box); System.Windows.Automation.AutomationProperties.SetName(box, label); }
        Field("表示名", NameBox); Field("アイコン（文字・絵文字）", IconBox); Field("自己紹介（会話の参加者へ共有）", _about); NameBox.Text = chat.Self.Name; IconBox.Text = chat.Self.Icon; _about.Text = chat.Self.About;
        string Snapshot() => JsonSerializer.Serialize(new { NameBox.Text, Icon = IconBox.Text, About = _about.Text }); string baseline = Snapshot(); bool saving = false;
        async Task<bool> Save() { if (saving) { return false; } saving = true; save.IsEnabled = false; string snapshot = Snapshot(); try { await chat.SaveSelfAsync(new(NameBox.Text, IconBox.Text, _about.Text)); baseline = snapshot; status.Text = "保存しました。過去の発言の表示名は当時のまま残します。"; return true; } catch (Exception ex) { status.Text = ex.Message; return false; } finally { saving = false; save.IsEnabled = true; } }
        save.Click += async (_, _) => await Save(); UnsavedChanges.Protect(this, () => Snapshot() != baseline, Save);
    }
}
