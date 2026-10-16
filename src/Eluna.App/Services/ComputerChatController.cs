using System.Diagnostics;
using System.Windows;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal sealed class ComputerChatController(ComputerSession session, Func<LlamaService> engineFactory, LocalModel model,
    AppSettings settings, Func<bool> sourceValid, Action changed)
{
    private static readonly SemaphoreSlim DesktopGate = new(1, 1);
    private readonly ComputerDesktop _desktop = new();
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _segment;
    private TaskCompletionSource<bool>? _selection, _approval, _resume;
    private IReadOnlyList<ComputerTarget> _allowed = [];
    private IReadOnlyList<ComputerTarget> _newSelection = [];
    private bool _pause, _takeover, _steering;
    private ComputerUseOverlay? _overlay;
    internal Task Completion { get; private set; } = Task.CompletedTask;

    internal void Start()
    {
        session.Command = command => _ = HandleAsync(command);
        session.Steer = Steer; session.ModelName = model.DisplayName;
        Completion = RunAsync(); session.Completion = Completion;
    }
    internal void Stop() { _overlay?.SetActive(false); try { _stop.Cancel(); _segment?.Cancel(); } catch (ObjectDisposedException) { } }
    private void State(string stage, string detail) { session.Stage = stage; session.Detail = detail; _overlay?.SetActive(stage == "running"); changed(); }
    private async Task HandleAsync(string command)
    {
        try
        {
            switch (command)
            {
                case "refresh" when session.Selecting: await RefreshAsync(_stop.Token); break;
                case "launch" when session.Selecting && session.SelectedApplication is { } app:
                    await Task.Run(() => ComputerApplications.Launch(app), _stop.Token);
                    session.Detail = "アプリを起動しました。表示されたら一覧を更新し、操作を許可してください。"; changed(); break;
                case "allow" when session.Selecting:
                    ComputerTarget[] selected = session.Windows.Where(w => w.Selected).Select(w => w.Target).ToArray();
                    if (selected.Length is < 1 or > 8) { session.Detail = "操作を許可するアプリを1〜8個選んでください。"; return; }
                    var combined = _allowed.Concat(selected).DistinctBy(w => w.Handle).ToArray();
                    if (combined.Length > 8) { session.Detail = "この依頼で使うアプリは合計8個までです。"; return; }
                    long[] previous = _allowed.Select(w => w.Handle).ToArray();
                    _newSelection = selected.OrderBy(w => previous.Contains(w.Handle)).ToArray(); _allowed = combined;
                    _selection?.TrySetResult(true); break;
                case "approve" when session.Review is { } proposal:
                    if (proposal.Action.Type is "type_text" or "set_value") { proposal.ApprovedAction = proposal.Action with { Text = session.ReviewInput }; }
                    _approval?.TrySetResult(true); break;
                case "deny": Stop(); break;
                case "pause": _overlay?.SetActive(false); _pause = true; _segment?.Cancel(); break;
                case "takeover": _overlay?.SetActive(false); _pause = _takeover = true; _segment?.Cancel(); break;
                case "resume": _resume?.TrySetResult(true); break;
                case "stop": Stop(); break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { session.Detail = ex.Message; changed(); }
    }
    private void Steer(string text)
    {
        if (text.Length > 4000 || session.Instructions.Count >= 40) { session.Detail = "追加指示は1回4,000文字、40回までです。"; return; }
        session.Instructions.Add(text); _steering = true; _pause = false; _takeover = false;
        _overlay?.SetActive(false);
        _resume?.TrySetResult(true); _segment?.Cancel(); changed();
    }
    private async Task RefreshAsync(CancellationToken token)
    {
        List<ComputerTarget> windows = await _desktop.WindowsAsync(token);
        List<ComputerInstalledApp> applications = await Task.Run(ComputerApplications.List, token);
        session.Applications.Clear(); foreach (ComputerInstalledApp app in applications) { session.Applications.Add(app); }
        long[] previous = session.Windows.Where(w => w.Selected).Select(w => w.Target.Handle).ToArray(); session.Windows.Clear();
        foreach (ComputerTarget window in windows) { session.Windows.Add(new(window) { Selected = previous.Contains(window.Handle) }); }
        session.Detail = "許可したアプリでマウスとキーボードを自動操作します。重要な操作は別に確認します。"; changed();
    }
    private async Task<IReadOnlyList<ComputerTarget>> SelectAppsAsync(string reason, CancellationToken token)
    {
        _selection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        State("selecting", reason); await RefreshAsync(token); session.Detail = reason;
        using CancellationTokenRegistration registration = token.Register(() => _selection.TrySetCanceled(token));
        try { await _selection.Task; return _newSelection; }
        finally { _selection = null; if (!token.IsCancellationRequested) { State("running", "許可したアプリの最新画面を確認します。"); } }
    }
    private async Task<bool> ReviewAsync(ComputerReview review, CancellationToken token)
    {
        session.Review = review; session.ReviewInput = review.Action.Text; session.Observe(review.Observation);
        _approval = new(TaskCreationOptions.RunContinuationsAsynchronously); State("review", "重要な操作の確認待ちです。内容を確認してから実行してください。");
        if (Application.Current.MainWindow is { } main) { if (main.WindowState == WindowState.Minimized) { main.WindowState = WindowState.Normal; } main.Activate(); }
        using CancellationTokenRegistration registration = token.Register(() => _approval?.TrySetCanceled(token));
        try { return await _approval.Task; }
        finally { _approval = null; session.Review = null; if (!token.IsCancellationRequested) { State("running", "確認した操作を実行します。"); } }
    }
    private string TaskText()
    {
        if (session.Instructions.Count == 0) { return session.Task; }
        string latest = "最新の利用者指示: " + session.Instructions[^1];
        string earlier = string.Join('\n', session.Instructions.SkipLast(1).TakeLast(4));
        if (earlier.Length > 800) { earlier = earlier[^800..]; }
        int room = 7900 - latest.Length - earlier.Length;
        string original = session.Task.Length <= room ? session.Task : session.Task[..room] + "\n[背景・過去文脈の末尾を省略]";
        return original + "\n\n以前の追加指示:\n" + earlier + "\n\n" + latest;
    }
    private async Task RunAsync()
    {
        try
        {
            if (session.SecondsUsed >= 1200 || session.Steps.Count >= session.MaxSteps) { State("limit", "操作回数または実行時間の上限に達しています。新しい依頼として送信してください。"); return; }
            ComputerDesktopNative.RequireUnlockedDesktop();
            await SelectAppsAsync("この依頼で操作してよいアプリを選び、「許可して続行」を押してください。", _stop.Token);
            while (true)
            {
                _stop.Token.ThrowIfCancellationRequested();
                if (!sourceValid()) { throw new InvalidOperationException("会話の所属や参照先が変わったため停止しました。元の会話から依頼し直してください。"); }
                _pause = _takeover = _steering = false; _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
                using var segment = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); _segment = segment;
                bool acquired = false; var clock = new Stopwatch();
                try
                {
                    State("queued", "別のComputer Useが終わると操作します。会話は切り替えられます。");
                    await DesktopGate.WaitAsync(segment.Token); acquired = true;
                    ComputerDesktopNative.RequireUnlockedDesktop();
                    segment.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, 1200 - session.SecondsUsed)));
                    using var hotkey = new ComputerStopHotkey(Application.Current.MainWindow, Stop);
                    using var overlay = new ComputerUseOverlay(() => _ = HandleAsync("pause"), Stop); _overlay = overlay;
                    using LlamaService engine = engineFactory();
                    State("running", "最新の画面から作業を進めます。"); clock.Start();
                    var planner = new ComputerPlanner(engine, model, settings, text => { session.Detail = text; overlay.Status(text); changed(); });
                    if (session.Plan.Length == 0) { session.Plan = await planner.PlanAsync(TaskText(), segment.Token); changed(); }
                    var runner = new ComputerUseRunner(_desktop, planner);
                    ComputerUseResult result = await runner.RunAsync(TaskText(), _allowed, session.MaxSteps, false,
                        observation => { session.Observe(observation); overlay.Observe(observation); }, step => { session.Steps.Add(step); changed(); }, text => { session.Detail = text; overlay.Status(text); changed(); }, ReviewAsync, segment.Token,
                        new(() => session.Automatic, [.. session.Steps], async (reason, token) => await SelectAppsAsync("別のアプリを使うための許可が必要です: " + reason, token), Math.Max(1, 1200 - session.SecondsUsed), overlay.BeforeAction, overlay.AfterAction));
                    if (result.State == "needs_user") { _pause = _takeover = true; State("takeover", result.Message + "\n対象アプリで操作した後、「最新画面から再開」を押してください。"); }
                    else { State(result.State, result.Message); return; }
                }
                catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
                { if (!_steering) { State(_takeover ? "takeover" : "paused", "自動操作を止めました。対象アプリを操作できます。再開時は画面を取り直します。"); } }
                finally
                {
                    clock.Stop(); session.SecondsUsed = Math.Min(1300, session.SecondsUsed + clock.Elapsed.TotalSeconds); _segment = null;
                    _overlay = null;
                    session.Review = null; if (acquired) { DesktopGate.Release(); }
                    changed();
                }
                if (session.SecondsUsed >= 1200 || session.Steps.Count >= session.MaxSteps) { State("limit", "操作回数または実行時間の上限に達しました。"); return; }
                if (_steering) { continue; }
                if (_pause)
                {
                    using CancellationTokenRegistration registration = _stop.Token.Register(() => _resume.TrySetCanceled(_stop.Token));
                    await _resume.Task;
                }
            }
        }
        catch (OperationCanceledException) { State("stopped", "停止しました。実行済みの操作は取り消されません。"); }
        catch (Exception ex) { State("failed", ex.Message); }
        finally { session.Review = null; session.Command = null; session.Steer = null; _segment = null; _stop.Dispose(); changed(); }
    }
}
