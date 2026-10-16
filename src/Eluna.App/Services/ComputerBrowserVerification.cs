using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using Eluna.App.Models;

namespace Eluna.App.Services;

// End-to-end test: the real chat controller operates an isolated browser profile, never a user's tabs.
internal static class ComputerBrowserVerification
{
    internal static async Task RunAsync(AppStore store, string output, string metadata, string page)
    {
        Directory.CreateDirectory(output); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15)); CancellationToken token = timeout.Token;
        Process? browser = null; WindowsJob? job = null; MainWindow? main = null; ComputerSession? session = null;
        var listener = new TcpListener(IPAddress.Loopback, 0); Task? serving = null;
        async Task Until(Func<bool> condition) { while (!condition()) { await Task.Delay(100, token); } }
        try
        {
            ComputerDesktopNative.RequireUnlockedDesktop();
            byte[] html = await File.ReadAllBytesAsync(page, token); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string url = $"http://127.0.0.1:{port}/";
            serving = ServeAsync(listener, html, output, token);
            string edge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft/Edge/Application/msedge.exe");
            if (!File.Exists(edge)) { edge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft/Edge/Application/msedge.exe"); }
            if (!File.Exists(edge)) { throw new FileNotFoundException("この検証にはWindows標準のEdgeが必要です。", edge); }
            var start = new ProcessStartInfo(edge) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Normal };
            foreach (string argument in new[] { "--user-data-dir=" + Path.Combine(output, "browser-profile"), "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--new-window", "--window-size=1280,940", "--window-position=40,40", url }) { start.ArgumentList.Add(argument); }
            browser = Process.Start(start)!; job = new(browser); var desktop = new ComputerDesktop(); ComputerTarget? target = null;
            while (target is null) { target = (await desktop.WindowsAsync(token)).FirstOrDefault(w => w.ProcessId == browser.Id && w.Title.Contains("Eluna ブラウザ検証", StringComparison.Ordinal)); if (target is null) { await Task.Delay(200, token); } }
            await File.WriteAllTextAsync(Path.Combine(output, "ready.json"), JsonSerializer.Serialize(new { Url = url, Target = target }, ComputerJson.Options), token);
            await File.WriteAllTextAsync(Path.Combine(output, "desktop-before.txt"), ComputerDesktopNative.DesktopStatus(target), token);
            LocalModel model = JsonSerializer.Deserialize<LocalModel>(await File.ReadAllTextAsync(metadata, token), AppStore.JsonOptions)!;
            model = model with { Preferences = new(ContextSize: 8192, Temperature: 0.1, MaxOutputTokens: 1024, DefaultMode: "fast") };
            string folder = Path.Combine(store.ModelsDirectory, model.Id); Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "model.json"), JsonSerializer.Serialize(model, AppStore.JsonOptions), token);
            main = new MainWindow(store) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 }; main.Show();
            await Until(() => main.ViewModel.IsReady || main.ViewModel.HasError);
            if (!main.ViewModel.IsReady) { throw new InvalidOperationException(main.ViewModel.Error); }
            await main.ViewModel.ApplySettingsAsync(main.ViewModel.Settings with { Compute = main.ViewModel.ComputeChoices.Any(d => d.Id == "Vulkan0") ? "Vulkan0" : "cpu", InferenceMode = "fast" });
            await Task.Delay(700, token);
            await File.WriteAllTextAsync(Path.Combine(output, "desktop-ready.txt"), ComputerDesktopNative.DesktopStatus(target), token);
            string task = "開いているEluna ブラウザ検証のページだけを使って、次の作業をしてください。カタログでカテゴリをミニPC、最大価格を120000円、メモリ32GB以上、在庫ありのみに設定し、価格が安い順に並べてください。条件を満たす最安商品の詳細を開き、商品名・税込価格・メモリを確認してください。比較メモを別タブで開き、その商品名・価格（数字だけ）・メモリ（数字だけ）を入力してください。用途は「ローカルAI用」、受け取り方法は「店頭受取」にして「下書きを保存」を押してください。保存済み表示と入力内容を確認してから完了を報告してください。実際の購入や別サイトへの移動は不要です。";
            await File.WriteAllTextAsync(Path.Combine(output, "task.txt"), task, token);
            main.ViewModel.Input = "@Computer " + task; main.ViewModel.SendCommand.Execute(null);
            await Until(() => main.ViewModel.SelectedConversation!.Messages.Any(m => m.Computer is not null));
            session = main.ViewModel.SelectedConversation!.Messages.Last(m => m.Computer is not null).Computer!;
            await Until(() => session.Selecting && session.Windows.Count > 0);
            session.Windows.Clear(); session.Windows.Add(new(target) { Selected = true }); session.MaxSteps = 60; session.Invoke("allow");
            int savedSteps = -1, reviews = 0; string lastStage = "";
            while (!session.Completion.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                if (savedSteps != session.Steps.Count || lastStage != session.Stage)
                {
                    savedSteps = session.Steps.Count; lastStage = session.Stage;
                    await File.WriteAllTextAsync(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { session.Stage, session.Detail, Steps = session.Steps }, ComputerJson.Options), token);
                    if (session.Observation is { } observation)
                    {
                        await File.WriteAllBytesAsync(Path.Combine(output, $"step-{savedSteps:00}.jpg"), Convert.FromBase64String(observation.ImageBase64), token);
                        await File.WriteAllTextAsync(Path.Combine(output, "observation.json"), JsonSerializer.Serialize(observation with { ImageBase64 = "" }, ComputerJson.Options), token);
                    }
                }
                if (session.Stage == "review" && session.Review is { } review)
                {
                    // This test's only consequential operation is writing a fake local draft.
                    string? name = review.Observation.Elements.FirstOrDefault(e => e.Id == review.Action.Element)?.Name;
                    if (review.Action.Element == 0 && review.Action.Type == "click")
                    {
                        ComputerBounds bounds = review.Observation.Bounds;
                        int x = bounds.Left + (int)((bounds.Width - 1L) * review.Action.X / 1000), y = bounds.Top + (int)((bounds.Height - 1L) * review.Action.Y / 1000);
                        name = review.Observation.Elements.FirstOrDefault(e => e.Type == "Button" && e.Bounds.Contains(x, y))?.Name;
                    }
                    bool permitted = (review.Action.Type == "click" || review.Action.Type == "key" && review.Action.Keys is "Enter" or "Return") && name == "下書きを保存" && review.Observation.Title.Contains("比較メモ", StringComparison.Ordinal);
                    if (!permitted) { throw new InvalidOperationException("想定外の確認操作: " + JsonSerializer.Serialize(review.Action, ComputerJson.Options)); }
                    reviews++; session.Invoke("approve");
                }
                if (session.Stage is "takeover" or "paused" or "selecting") { throw new InvalidOperationException("自律実行が中断しました: " + session.Detail); }
                await Task.Delay(100, token);
            }
            await session.Completion; await main.ViewModel.PersistSafelyAsync();
            string draftFile = Path.Combine(output, "draft.json"); bool saved = File.Exists(draftFile), correct = false;
            if (saved)
            {
                using JsonDocument draft = JsonDocument.Parse(await File.ReadAllTextAsync(draftFile, token)); JsonElement d = draft.RootElement, f = d.GetProperty("filters");
                correct = d.GetProperty("product").GetString() == "North Mini 32" && d.GetProperty("price").GetString() == "99800" && d.GetProperty("ram").GetString() == "32" && d.GetProperty("purpose").GetString() == "ローカルAI用" && d.GetProperty("delivery").GetString() == "店頭受取" && d.GetProperty("detail").GetString() == "mini-beta" && f.GetProperty("category").GetString() == "mini" && f.GetProperty("budget").GetString() == "120000" && f.GetProperty("memory").GetBoolean() && f.GetProperty("stock").GetBoolean() && f.GetProperty("sort").GetString() == "price";
            }
            ComputerObservation final = await desktop.ObserveAsync(target, token);
            bool displayed = final.Elements.Any(e => (e.Name + e.Value).Contains("下書きを保存しました", StringComparison.Ordinal));
            await File.WriteAllBytesAsync(Path.Combine(output, "final.jpg"), Convert.FromBase64String(final.ImageBase64), token);
            await PrototypePreview.RenderAsync(main, Path.Combine(output, "chat-result.png"));
            await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { Success = correct && displayed && session.Stage == "complete", Saved = saved, Correct = correct, Displayed = displayed, session.Stage, session.Detail, Model = model.DisplayName, Compute = main.ViewModel.Settings.Compute, Reviews = reviews, session.SecondsUsed, Steps = session.Steps }, ComputerJson.Options), token);
        }
        catch (Exception ex)
        {
            session?.Invoke("stop"); if (session is not null) { await session.Completion; }
            await File.WriteAllTextAsync(Path.Combine(output, "error.txt"), ex.ToString());
            await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { Success = false, BlockedByLock = ComputerDesktopNative.IsDesktopLocked(), Stage = session?.Stage, Detail = ex.Message, Steps = session?.Steps }, ComputerJson.Options));
        }
        finally
        {
            timeout.Cancel(); listener.Stop(); if (serving is not null) { try { await serving; } catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { } }
            if (main is not null) { await main.ViewModel.ShutdownAsync(); main.Close(); }
            job?.Dispose(); browser?.Dispose(); Application.Current.Shutdown();
        }
    }

    internal static async Task ServeAsync(TcpListener listener, byte[] html, string output, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(token); using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token); requestTimeout.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                await using NetworkStream stream = client.GetStream(); var header = new List<byte>(); byte[] single = new byte[1];
                while (header.Count < 8192)
                {
                    if (await stream.ReadAsync(single, requestTimeout.Token) == 0) { break; }
                    header.Add(single[0]); int n = header.Count;
                    if (n >= 4 && header[n - 4] == 13 && header[n - 3] == 10 && header[n - 2] == 13 && header[n - 1] == 10) { break; }
                }
                string[] lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.None); string[] first = lines[0].Split(' ');
                if (first.Length < 2) { continue; }
                string? lengthLine = lines.FirstOrDefault(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                int length = lengthLine is null ? 0 : int.Parse(lengthLine[15..], System.Globalization.CultureInfo.InvariantCulture);
                if (length is < 0 or > 16384) { continue; }
                byte[] body = new byte[length]; await stream.ReadExactlyAsync(body, requestTimeout.Token);
                byte[] response = html; string type = "text/html; charset=utf-8", status = "200 OK";
                if (first[0] == "POST" && first[1] == "/api/draft")
                {
                    using JsonDocument data = JsonDocument.Parse(body); await File.WriteAllBytesAsync(Path.Combine(output, "draft.json"), body, token);
                    response = "{}"u8.ToArray(); type = "application/json";
                }
                else if (first[0] != "GET" || first[1] is not ("/" or "/memo")) { status = "404 Not Found"; response = []; }
                byte[] prefix = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {response.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
                await stream.WriteAsync(prefix, requestTimeout.Token); await stream.WriteAsync(response, requestTimeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or FormatException or JsonException) { }
        }
    }
}
