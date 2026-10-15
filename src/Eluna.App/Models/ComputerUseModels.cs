using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eluna.App.Models;

public sealed record ComputerTarget(long Handle, int ProcessId, long Started, string Title, string App)
{
    public string Label => $"{Title}  ·  {App}";
}

public sealed record ComputerBounds(int Left, int Top, int Width, int Height)
{
    public bool Contains(int x, int y) => x >= Left && y >= Top && x < Left + Width && y < Top + Height;
}

public sealed record ComputerElement(int Id, string RuntimeId, string Name, string Type, string Value,
    ComputerBounds Bounds, bool Password, bool Enabled, string[] Patterns);

public sealed record ComputerObservation(ComputerTarget Target, long Surface, string Title, ComputerBounds Bounds,
    List<ComputerElement> Elements, string ImageBase64, int ImageWidth, int ImageHeight, byte[] Signature, string Notice = "");

public sealed record ComputerAction(string Type, string Reason = "", int Element = 0, int X = 0, int Y = 0,
    int X2 = 0, int Y2 = 0, string Text = "", string Keys = "", int Amount = 1, int Window = 0)
{
    public string Label => Type switch
    {
        "click" => "クリック",
        "double_click" => "ダブルクリック",
        "right_click" => "右クリック",
        "type_text" => "文字を入力",
        "set_value" => "入力欄を置き換え",
        "key" => "キー操作 · " + Keys,
        "drag" => "ドラッグ",
        "scroll" => Amount > 0 ? "下へスクロール" : "上へスクロール",
        "switch_window" => "対象アプリを切り替え",
        "wait" => "画面の更新を待つ",
        "finish" => "完了報告",
        "ask" => "利用者へ確認",
        _ => "不明な操作"
    };
}

public sealed record ComputerStep(int Number, DateTimeOffset At, string Window, ComputerAction Action, string Result);
public sealed record ComputerReview(ComputerObservation Observation, ComputerAction Action, string ConfirmationReason)
{
    internal ComputerAction? ApprovedAction { get; set; }
}
public sealed record ComputerUseResult(string State, string Message, int Steps);

internal static class ComputerJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
}

internal sealed record ComputerWorkerRequest(string Operation, int Parent, ComputerTarget? Target = null,
    ComputerObservation? Observation = null, ComputerAction? Action = null);
internal sealed record ComputerWorkerResponse(List<ComputerTarget>? Targets = null, ComputerObservation? Observation = null,
    string? Error = null, bool Changed = false);
