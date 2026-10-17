using System.Windows;
using System.Windows.Controls;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App;

internal sealed class VoiceParticipantsWindow : Window
{
    internal VoiceParticipantsWindow(ChatViewModel chat)
    {
        Title = "ボイスの参加者 · Eluna"; Width = 470; Height = 540; MinWidth = 350; MinHeight = 350; WindowStartupLocation = WindowStartupLocation.CenterOwner; Appearance.Attach(this);
        Guid room = chat.VoiceRoomId ?? throw new InvalidOperationException("先にボイスチャンネルへ参加してください。"); long session = chat.VoiceSession;
        var root = new DockPanel { Margin = new(20) }; Content = root;
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) }; footer.Children.Add(status);
        var save = new Button { Content = "参加者を更新", HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(save);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock { Text = "このPCの音声セッションへ参加するアカウント", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) });
        body.Children.Add(new TextBlock { Text = "チャンネルの登録メンバーとは別の設定です。参加したアカウントの完成した発言だけを読み上げます。退出・再起動時に接続を解除します。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 14) });
        foreach (var account in chat.EligibleVoiceMembers)
        { body.Children.Add(new CheckBox { Content = new TextBlock { Text = account.Label, TextWrapping = TextWrapping.Wrap }, Tag = account.Id, IsChecked = chat.VoiceParticipants.Any(p => p.Id == account.Id), Margin = new(0, 8, 0, 8) }); }
        save.Click += async (_, _) =>
        { try { await chat.SetVoiceParticipantsAsync(room, session, body.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (Guid)c.Tag).ToArray()); Close(); } catch (Exception ex) { status.Text = ex.Message; } };
    }
}
