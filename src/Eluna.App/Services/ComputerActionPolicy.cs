using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class ComputerActionPolicy
{
    internal static readonly string[] Types = ["click", "double_click", "right_click", "type_text", "set_value", "key", "drag", "scroll", "switch_window", "wait", "finish", "ask"];
    internal static readonly object Schema = new
    {
        type = "object",
        additionalProperties = false,
        properties = new
        {
            type = new { type = "string", @enum = Types },
            reason = new { type = "string", maxLength = 180 },
            element = new { type = "integer", minimum = 0, maximum = 256 },
            x = new { type = "integer", minimum = 0, maximum = 1000 },
            y = new { type = "integer", minimum = 0, maximum = 1000 },
            x2 = new { type = "integer", minimum = 0, maximum = 1000 },
            y2 = new { type = "integer", minimum = 0, maximum = 1000 },
            text = new { type = "string", maxLength = 8000 },
            keys = new { type = "string", maxLength = 60 },
            amount = new { type = "integer", minimum = -5, maximum = 5 },
            window = new { type = "integer", minimum = 0, maximum = 8 }
        },
        required = new[] { "type", "reason", "element", "x", "y", "x2", "y2", "text", "keys", "amount", "window" }
    };

    internal static ComputerAction Parse(string json)
    {
        if (json.Length > 24000) { throw new InvalidDataException("モデルの操作案が長すぎます。"); }
        ComputerAction action = JsonSerializer.Deserialize<ComputerAction>(json, ComputerJson.Options) ?? throw new InvalidDataException("操作案が空です。");
        Validate(action); return action;
    }

    internal static void Validate(ComputerAction action)
    {
        if (!Types.Contains(action.Type) || action.Reason is null || action.Text is null || action.Keys is null ||
            action.Reason.Length > 2000 || action.Text.Length > 8000 || action.Keys.Length > 60 || action.Element is < 0 or > 256 ||
            action.X is < 0 or > 1000 || action.Y is < 0 or > 1000 || action.X2 is < 0 or > 1000 || action.Y2 is < 0 or > 1000 ||
            action.Amount is < -5 or > 5 || action.Window is < 0 or > 8)
        { throw new InvalidDataException("モデルの操作案が対応範囲外です。操作は実行していません。"); }
        if (action.Type == "key") { _ = Keys(action.Keys); }
        if (action.Type == "set_value" && action.Element == 0) { throw new InvalidDataException("入力欄の指定がありません。"); }
        if (action.Type == "switch_window" && action.Window == 0) { throw new InvalidDataException("切り替え先の指定がありません。"); }
    }

    internal static ushort[] Keys(string chord)
    {
        string[] parts = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4) { throw new InvalidDataException("キーはCtrl+Lのように1組で指定してください。"); }
        var keys = new List<ushort>();
        foreach (string part in parts)
        {
            string key = part.ToUpperInvariant();
            ushort code = key switch
            {
                "CTRL" or "CONTROL" => 0x11,
                "SHIFT" => 0x10,
                "ALT" => 0x12,
                "ENTER" or "RETURN" => 0x0D,
                "ESC" or "ESCAPE" => 0x1B,
                "TAB" => 0x09,
                "SPACE" => 0x20,
                "BACKSPACE" => 0x08,
                "DELETE" => 0x2E,
                "LEFT" => 0x25,
                "UP" => 0x26,
                "RIGHT" => 0x27,
                "DOWN" => 0x28,
                "HOME" => 0x24,
                "END" => 0x23,
                "PAGEUP" => 0x21,
                "PAGEDOWN" => 0x22,
                "F5" => 0x74,
                "F6" => 0x75,
                _ when key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]) => key[0],
                _ => throw new InvalidDataException("そのキーは操作対象外です。Windowsキー、タスク切り替え、クリップボード貼り付けは利用者が操作してください。")
            };
            keys.Add(code);
        }
        if (keys.Distinct().Count() != keys.Count || keys.Take(keys.Count - 1).Any(k => k is not (0x10 or 0x11 or 0x12)) ||
            keys[^1] is 0x10 or 0x11 or 0x12 || keys.Contains((ushort)0x12) && keys.Contains((ushort)0x09) ||
            keys.Contains((ushort)0x11) && keys[^1] is 0x56 or 0x58 || keys.Contains((ushort)0x11) && keys.Contains((ushort)0x10) && keys[^1] == 0x1B)
        { throw new InvalidDataException("そのキーの組み合わせは対象外です。操作は実行していません。"); }
        return [.. keys];
    }

    internal static string? Confirmation(ComputerAction action, ComputerObservation observation, bool navigationOnlyAuto)
    {
        if (action.Type is "finish" or "wait") { return null; }
        if (action.Type == "ask") { return "利用者の判断が必要です。操作せずに終了します。"; }
        if (action.Element > 0 && observation.Elements.FirstOrDefault(e => e.Id == action.Element) is { Password: true })
        { throw new InvalidOperationException("パスワード欄は操作しません。対象アプリで利用者が入力してください。"); }
        if (navigationOnlyAuto)
        {
            if (action.Type is "scroll" or "switch_window") { return null; }
            if (action.Type == "key" && Regex.IsMatch(action.Keys, @"^(Tab|Shift\+Tab|Escape|Esc|Left|Right|Up|Down|PageUp|PageDown|Home|End)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) { return null; }
        }
        return action.Type is "type_text" or "set_value" ? "表示中のアプリへ次の文字を入力します。送信先や内容を確認してください。" :
            "クリックやキー操作は送信・保存・削除等につながる場合があります。対象と画面を確認してください。";
    }

    internal static void RequireFresh(ComputerObservation before, ComputerObservation now, ComputerAction action)
    {
        if (before.Target != now.Target || before.Surface != now.Surface || before.Bounds != now.Bounds)
        { throw new ComputerScreenChangedException("対象や位置が変化"); }
        var previousUi = before.Elements.Select(e => (e.RuntimeId, e.Name, e.Type, e.Value, e.Bounds, e.Password, e.Enabled));
        var currentUi = now.Elements.Select(e => (e.RuntimeId, e.Name, e.Type, e.Value, e.Bounds, e.Password, e.Enabled));
        if (!previousUi.SequenceEqual(currentUi)) { throw new ComputerScreenChangedException("UI要素の構成が変化"); }
        if (action.Element > 0)
        {
            ComputerElement expected = before.Elements.FirstOrDefault(e => e.Id == action.Element) ?? throw new ComputerScreenChangedException();
            ComputerElement? current = now.Elements.FirstOrDefault(e => e.RuntimeId == expected.RuntimeId);
            if (current is null || current.Name != expected.Name || current.Type != expected.Type || current.Value != expected.Value ||
                current.Bounds != expected.Bounds || current.Password || !current.Enabled) { throw new ComputerScreenChangedException("指定した要素が変化"); }
        }
        // Cursor blink and the active title bar can change; a materially changed image requires a new proposal.
        if (before.Signature.Length != now.Signature.Length || before.Signature.Length == 0 ||
            before.Signature.Zip(now.Signature).Average(p => Math.Abs(p.First - p.Second)) > 8)
        { throw new ComputerScreenChangedException("画面の画像が変化"); }
    }
}

internal sealed class ComputerScreenChangedException(string category = "画面が変化") : InvalidOperationException("確認後に" + category + "しました。最新の画面から操作を考え直します。")
{ internal string Category { get; } = category; }
