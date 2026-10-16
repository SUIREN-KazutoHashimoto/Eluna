using System.Windows;
using System.Windows.Controls;
using Eluna.App.Services;
using Eluna.App.ViewModels;

namespace Eluna.App.Controls;

public partial class VoiceChannelPanel : UserControl
{
    public VoiceChannelPanel() => InitializeComponent();
    private ChatViewModel? Chat => DataContext as ChatViewModel;
    private async void Join_Click(object sender, RoutedEventArgs e) { if (Chat is { } chat) { await chat.JoinVoiceAsync(); } }
    private async void Microphone_Click(object sender, RoutedEventArgs e) { if (Chat is { } chat) { await chat.ToggleMicrophoneAsync(); } }
    private async void Interrupt_Click(object sender, RoutedEventArgs e) { if (Chat is { } chat) { await chat.InterruptVoiceAsync(); } }
    private async void Output_Click(object sender, RoutedEventArgs e) { if (Chat is { } chat) { await chat.ToggleVoiceOutputAsync(); } }
    private async void Leave_Click(object sender, RoutedEventArgs e) { if (Chat is { } chat) { await chat.LeaveVoiceAsync(); } }
    private void Open_Click(object sender, RoutedEventArgs e) => Chat?.OpenVoiceChannel();
    private void StopShare_Click(object sender, RoutedEventArgs e) => Chat?.StopSharing();
    private void Share_Click(object sender, RoutedEventArgs e)
    {
        if (Chat is not { IsJoinedVoiceChannel: true } chat) { Chat?.OpenVoiceChannel(); return; }
        new ScreenShareWindow(chat) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
