using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    public string ComposerHint => (_settings.ControlEnterToSend ? "Ctrl+Enter 送信 · Enter 改行" : "Enter 送信 · Shift+Enter 改行") + (Input.Length > 2000 ? " · " + Input.Length.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + "文字" : "");
    internal async Task RememberEmojiAsync(string glyph)
    {
        if (!EmojiCatalog.Contains(glyph)) { return; }
        _settings = _settings with { RecentEmoji = EmojiCatalog.Remember(glyph, _settings.RecentEmoji) }; Notify(nameof(Settings)); await PersistSafelyAsync();
    }
}
