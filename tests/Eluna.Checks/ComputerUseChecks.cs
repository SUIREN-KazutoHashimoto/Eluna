using System.IO;
using System.Text.Json;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.Checks;

internal static class ComputerUseChecks
{
    private static void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
    private static void Reject(Action action, string message) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or JsonException) { return; } throw new InvalidOperationException(message); }
    private static readonly ComputerTarget Target = new(101, 201, 301, "テスト対象", "fixture");
    private static ComputerObservation Screen() => new(Target, 101, Target.Title, new(-1200, 100, 1000, 700),
        [new(1, "fixture.1", "入力欄", "Edit", "", new(-1150, 150, 600, 40), false, true, ["value"]),
         new(2, "fixture.2", "送信", "Button", "", new(-1150, 200, 100, 40), false, true, ["invoke"]),
         new(3, "fixture.3", "password", "Edit", "［非表示］", new(-1150, 250, 100, 40), true, true, ["value"])], "", 1000, 700, new byte[768]);

    internal static async Task RunAsync()
    {
        ComputerObservation screen = Screen();
        Check(ComputerUseOverlay.ActionArea(screen, new("click", X: 1000, Y: 1000)) is { Left: -229, Top: 771 }, "Overlay coordinates lost the negative monitor origin or last pixel.");
        Check(ComputerUseOverlay.ActionArea(screen, new("set_value", Element: 1)) == screen.Elements[0].Bounds, "Typing highlight lost the element bounds.");
        Check(ComputerUseOverlay.ActionArea(screen, new("click", Element: 3)) is null, "Overlay highlighted a password field.");
        var work = new ComputerBounds(-1920, -200, 1920, 1080);
        ComputerBounds bottom = ComputerUseOverlay.BarPlacement(work, 490, 68, null)!;
        Check(bottom.Left >= work.Left && bottom.Top + bottom.Height <= work.Top + work.Height, "Overlay bar left the monitor work area.");
        Check(ComputerUseOverlay.BarPlacement(work, 490, 68, bottom) is { Top: -188 }, "Overlay bar did not avoid an action under its controls.");
        Check(ComputerUseOverlay.BarPlacement(work, 490, 68, work) is null, "Overlay controls covered a drag spanning the monitor.");
        Check(ComputerUseOverlay.BarPlacement(new(0, 0, 200, 100), 490, 68, null) is null, "Overlay controls were clipped on an undersized work area.");
        ComputerAction action = new("set_value", "日本語を入力", 1, Text: "テスト文章");
        Check(ComputerActionPolicy.Parse(JsonSerializer.Serialize(action, ComputerJson.Options)) == action, "Action JSON round trip changed Unicode.");
        Reject(() => ComputerActionPolicy.Parse("{\"type\":\"shell\",\"text\":\"run\"}"), "Unrecognized action was accepted.");
        Reject(() => ComputerActionPolicy.Parse("{\"type\":\"click\",\"extra\":\"ignore approval\"}"), "Unknown JSON fields were accepted.");
        Reject(() => ComputerActionPolicy.Validate(new("click", X: -1)), "Negative coordinates were accepted.");
        Reject(() => ComputerActionPolicy.Validate(new("type_text", Text: new string('x', 8001))), "Oversized text was accepted.");
        foreach (string key in new[] { "Win+R", "Alt+Tab", "Ctrl+Shift+Escape", "Ctrl+Ctrl+A", "A+B", "Ctrl" })
        { Reject(() => ComputerActionPolicy.Keys(key), "Unsafe or malformed chord was accepted: " + key); }
        Check(ComputerActionPolicy.Keys("Ctrl+L").SequenceEqual(new ushort[] { 0x11, 0x4C }), "Control chord parsing failed.");
        Check(ComputerActionPolicy.Confirmation(action, screen, true) is not null, "Automatic mode skipped typing confirmation.");
        Check(ComputerActionPolicy.Confirmation(new("click", Element: 2), screen, true) is not null, "Automatic mode skipped clicking confirmation.");
        Check(ComputerActionPolicy.Confirmation(new("key", Keys: "Enter"), screen, true) is not null, "Automatic mode skipped possible submission confirmation.");
        Check(ComputerActionPolicy.Confirmation(new("key", Keys: "Tab"), screen, true) is null, "Navigation mode did not permit Tab.");
        Check(ComputerActionPolicy.AutomaticConfirmation(action, screen) is null, "Automatic mode still required ordinary typing approval.");
        Check(ComputerActionPolicy.AutomaticConfirmation(new("click", Element: 1), screen) is null, "Automatic mode still required field click approval.");
        Check(ComputerActionPolicy.AutomaticConfirmation(new("click", Element: 2), screen) is not null, "Automatic mode submitted without review.");
        Check(ComputerActionPolicy.AutomaticConfirmation(new("click", Element: 1, Sensitive: true), screen) is not null, "A model risk flag was ignored.");
        Check(ComputerActionPolicy.AutomaticConfirmation(new("key", Keys: "Ctrl+A"), screen) is null, "Automatic keyboard selection required review.");
        Check(ComputerActionPolicy.AutomaticConfirmation(new("key", Keys: "Ctrl+Enter"), screen) is not null, "Submission key skipped review.");
        Check(ComputerActionPolicy.AutomaticConfirmation(new("key", Keys: "Ctrl+V"), screen) is not null, "Clipboard paste skipped review.");
        Check(ComputerActionPolicy.AutomaticConfirmation(new("key", Keys: "Control+Shift+V"), screen) is not null, "Clipboard shortcut alias skipped review.");
        Check(ComputerActionPolicy.AutomaticConfirmation(new("key", Keys: "Control+Shift+Enter"), screen) is not null, "Modified submission key skipped review.");
        Reject(() => ComputerApplications.Launch(new("unregistered", Path.Combine(Path.GetTempPath(), "not-a-registered-app.exe"), "")), "Arbitrary executable launch was accepted.");
        Reject(() => ComputerApplications.Launch(new("unregistered", Path.Combine(Path.GetTempPath(), "not-a-registered-app.lnk"), "")), "Unregistered shortcut launch was accepted.");
        Check(Eluna.App.ViewModels.ChatViewModel.RequestsComputer("@Computer 作業して", "chat") && Eluna.App.ViewModels.ChatViewModel.RequestsComputer("入力して", "computer") && !Eluna.App.ViewModels.ChatViewModel.RequestsComputer("Computer Useとは何ですか", "chat"), "Computer task routing confused explanation and execution.");
        var savedTask = new SavedComputerTask("依頼", "model", "running", "計画", [], [], true);
        var restoredTask = ComputerSession.Restore(savedTask);
        Check(restoredTask.Stage == "interrupted" && restoredTask.Windows.Count == 0 && restoredTask.Preview is null && !restoredTask.Pending, "Restore resumed or retained app permission/screenshots.");
        Check(ComputerActionPolicy.ValidSaved(savedTask) && !ComputerActionPolicy.ValidSaved(savedTask with { SecondsUsed = double.NaN }), "Malformed computer history was accepted.");
        var exhausted = new ComputerSession { Task = "上限", ModelId = "fixture", SecondsUsed = 1200 };
        var exhaustedController = new ComputerChatController(exhausted, () => throw new InvalidOperationException("Should not load a model."), new("fixture", "fixture", "local", "local", "fixture.gguf", "", 0), new(), () => true, () => { });
        exhaustedController.Start(); await exhaustedController.Completion; exhaustedController.Stop(); exhaustedController.Stop();
        Check(exhausted.Stage == "limit" && exhausted.Windows.Count == 0, "A completed or exhausted controller was not safe to stop again.");
        Reject(() => ComputerActionPolicy.Confirmation(new("type_text", Element: 3), screen, false), "Password field could be modified.");
        ComputerActionPolicy.RequireFresh(screen, screen, action);
        Reject(() => ComputerActionPolicy.RequireFresh(screen, screen with { Surface = 102 }, action), "Changed modal window was accepted.");
        Reject(() => ComputerActionPolicy.RequireFresh(screen, screen with { Bounds = screen.Bounds with { Left = -1199 } }, action), "Moved window was accepted.");
        Reject(() => ComputerActionPolicy.RequireFresh(screen, screen with { Signature = Enumerable.Repeat((byte)255, 768).ToArray() }, action), "Changed screen image was accepted.");
        Reject(() => ComputerActionPolicy.RequireFresh(screen, screen with { Elements = [screen.Elements[0] with { Name = "別の入力欄" }] }, action), "Replaced element was accepted.");

        var desktop = new FakeDesktop(screen); var planner = new FakePlanner([action, new("click", Element: 2), new("finish", "保存結果を確認")]);
        var log = new List<ComputerStep>(); int reviews = 0, observations = 0, effectsBefore = 0, effectsAfter = 0;
        ComputerUseResult result = await new ComputerUseRunner(desktop, planner).RunAsync("入力して保存", [Target], 10, true,
            _ => observations++, log.Add, _ => { }, (_, _) => { reviews++; return Task.FromResult(true); }, CancellationToken.None,
            new(() => false, [], BeforeAction: (_, _) => { Check(reviews > effectsBefore, "Effects displayed before review approval."); effectsBefore++; }, AfterAction: () => effectsAfter++));
        Check(result.State == "complete" && desktop.Executed == 2 && reviews == 2 && observations == 3 && log.Count == 2, "Observe / confirm / execute / reobserve loop failed.");
        Check(effectsBefore == 2 && effectsAfter == 2, "Action effects did not bracket execution.");
        await Task.Delay(40);
        desktop = new(screen); planner = new([action]);
        result = await new ComputerUseRunner(desktop, planner).RunAsync("入力", [Target], 5, false, _ => { }, _ => { }, _ => { }, (_, _) => Task.FromResult(false), CancellationToken.None,
            new(() => false, [], BeforeAction: (_, _) => throw new InvalidOperationException("Declined action displayed execution effects.")));
        Check(result.State == "stopped" && desktop.Executed == 0, "Rejected action was executed.");
        await Task.Delay(40);
        desktop = new(screen); planner = new([action, new("click", Element: 2), new("finish")]); reviews = 0;
        result = await new ComputerUseRunner(desktop, planner).RunAsync("入力して送信", [Target], 5, false, _ => { }, _ => { }, _ => { }, (_, _) => { reviews++; return Task.FromResult(true); }, CancellationToken.None, new(() => true, []));
        Check(result.State == "complete" && desktop.Executed == 2 && reviews == 1, "Automatic mouse/keyboard did not preserve the consequential-action boundary.");
        await Task.Delay(40);
        desktop = new(screen); planner = new([new("finish")]);
        result = await new ComputerUseRunner(desktop, planner).RunAsync("再開", [Target], 5, false, _ => { }, _ => { }, _ => { }, (_, _) => Task.FromResult(true), CancellationToken.None,
            new(() => true, [new(1, DateTimeOffset.UtcNow, "対象", action, "実行済み")]));
        Check(result.Steps == 1 && desktop.Executed == 0, "Resuming replayed completed input.");
        await Task.Delay(40);
        desktop = new(screen); planner = new([new("request_app", Text: "別のアプリ"), new("finish")]); int permissions = 0;
        var approvedTarget = Target with { Handle = 103 };
        result = await new ComputerUseRunner(desktop, planner).RunAsync("複数アプリ", [Target], 5, false, _ => { }, _ => { }, _ => { }, (_, _) => Task.FromResult(true), CancellationToken.None,
            new(() => true, [], (_, _) => { permissions++; return Task.FromResult<IReadOnlyList<ComputerTarget>>([approvedTarget]); }));
        Check(result.State == "complete" && permissions == 1 && desktop.Executed == 0, "New app approval was skipped or caused input before observation.");
        await Task.Delay(40);
        desktop = new(screen); planner = new([action, new("finish")]);
        result = await new ComputerUseRunner(desktop, planner).RunAsync("入力", [Target], 5, false, _ => { }, _ => { }, _ => { },
            (proposal, _) => { proposal.ApprovedAction = proposal.Action with { Text = "利用者が修正した文字" }; return Task.FromResult(true); }, CancellationToken.None);
        Check(result.State == "complete" && desktop.LastAction?.Text == "利用者が修正した文字", "Human correction was not used for input.");
        await Task.Delay(40);
        desktop = new(screen); planner = new([action]);
        try
        {
            await new ComputerUseRunner(desktop, planner).RunAsync("入力", [Target], 5, false, _ => { }, _ => { }, _ => { },
                (proposal, _) => { proposal.ApprovedAction = proposal.Action with { Element = 2 }; return Task.FromResult(true); }, CancellationToken.None);
            throw new Exception("Review changed the target element.");
        }
        catch (InvalidOperationException) { Check(desktop.Executed == 0, "Target changed after review."); }
        await Task.Delay(40);
        desktop = new(screen); planner = new([action]);
        result = await new ComputerUseRunner(desktop, planner).RunAsync("入力", [Target], 1, false, _ => { }, _ => { }, _ => { }, (_, _) => Task.FromResult(true), CancellationToken.None);
        Check(result.State == "limit" && desktop.Executed == 1, "Step limit did not stop the controller.");
        await Task.Delay(40);
        desktop = new(screen); planner = new([new("switch_window", Window: 2), new("finish")]);
        var secondTarget = Target with { Handle = 102, Title = "第2の許可対象" }; ComputerTarget? observedTarget = null;
        result = await new ComputerUseRunner(desktop, planner).RunAsync("切り替え", [Target, secondTarget], 5, true, o => observedTarget = o.Target, _ => { }, _ => { }, (_, _) => Task.FromResult(true), CancellationToken.None);
        Check(result.State == "complete" && observedTarget == secondTarget, "Allowed window switch observed the wrong window.");
        await Task.Delay(40);
        desktop = new(screen) { Changed = true }; planner = new([action, action, action]);
        effectsAfter = 0;
        result = await new ComputerUseRunner(desktop, planner).RunAsync("入力", [Target], 10, false, _ => { }, _ => { }, _ => { }, (_, _) => Task.FromResult(true), CancellationToken.None,
            new(() => false, [], AfterAction: () => effectsAfter++));
        Check(result.State == "needs_user" && desktop.Executed == 0, "Repeated screen changes did not stop safely.");
        Check(effectsAfter == 3, "Stale actions left execution effects active.");
        await Task.Delay(40);
        desktop = new(screen); planner = new([new("switch_window", Window: 2)]);
        try { await new ComputerUseRunner(desktop, planner).RunAsync("別画面", [Target], 5, true, _ => { }, _ => { }, _ => { }, (_, _) => Task.FromResult(true), CancellationToken.None); throw new Exception("Unauthorized window switch succeeded."); }
        catch (InvalidOperationException) { Check(desktop.Executed == 0, "Unauthorized switch caused input."); }
        await Task.Delay(40);
        using var cancel = new CancellationTokenSource(); desktop = new(screen); planner = new([action]);
        try
        {
            await new ComputerUseRunner(desktop, planner).RunAsync("入力", [Target], 5, false, _ => { }, _ => { }, _ => { },
                async (_, token) => { cancel.Cancel(); await Task.Delay(Timeout.Infinite, token); return true; }, cancel.Token);
            throw new Exception("Canceled approval did not stop.");
        }
        catch (OperationCanceledException) { Check(desktop.Executed == 0, "Input occurred after cancellation."); }
        await Task.Delay(40);
        using IDisposable lease = await ComputerUseLease.AcquireAsync(CancellationToken.None);
        try { using IDisposable second = await ComputerUseLease.AcquireAsync(CancellationToken.None); throw new Exception("Two controllers acquired the desktop."); } catch (InvalidOperationException) { }
        Console.WriteLine("Computer Use: action protocol, permission boundaries, stale screen, observe-act loop, denial, cancellation and exclusive input: OK");
    }

    private sealed class FakeDesktop(ComputerObservation screen) : IComputerDesktop
    {
        internal int Executed; internal bool Changed; internal ComputerAction? LastAction;
        public Task<ComputerObservation> ObserveAsync(ComputerTarget target, CancellationToken token) => Task.FromResult(screen with { Target = target });
        public Task ExecuteAsync(ComputerObservation observation, ComputerAction action, CancellationToken token)
        { if (Changed) { throw new ComputerScreenChangedException(); } token.ThrowIfCancellationRequested(); LastAction = action; Executed++; return Task.CompletedTask; }
    }
    private sealed class FakePlanner(IEnumerable<ComputerAction> actions) : IComputerPlanner
    {
        private readonly Queue<ComputerAction> _actions = new(actions);
        public Task<ComputerAction> NextAsync(string task, ComputerObservation observation, IReadOnlyList<ComputerTarget> allowed, IReadOnlyList<ComputerStep> steps, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(_actions.Dequeue()); }
    }
}
