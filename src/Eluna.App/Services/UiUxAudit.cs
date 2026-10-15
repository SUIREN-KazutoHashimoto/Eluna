using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Eluna.App.Models;

namespace Eluna.App.Services;

// Runs only with --capture-dir <isolated directory> --audit-ui. Uses synthetic data.
internal static class UiUxAudit
{
    public static async Task RunAsync(MainWindow window, AppStore store, string output)
    {
        Directory.CreateDirectory(output);
        var observations = new Dictionary<string, object>();
        var fields = new List<object>();
        async Task Capture(Window target, string name)
        {
            await PrototypePreview.RenderAsync(target, Path.Combine(output, name + ".png"));
            foreach (Control field in Descendants(target).OfType<Control>().Where(c => c.IsVisible && c is TextBox or ComboBox))
            {
                AutomationPeer? peer = UIElementAutomationPeer.CreatePeerForElement(field);
                fields.Add(new { screen = name, type = field.GetType().Name, id = field.Name, name = peer?.GetName() ?? "", readOnly = field is TextBox { IsReadOnly: true } });
            }
        }
        try
        {
            for (int i = 0; i < 600 && !window.ViewModel.IsReady && !window.ViewModel.HasError; i++) { await Task.Delay(100); }
            if (!window.ViewModel.IsReady) { throw new InvalidOperationException(window.ViewModel.Error); }
            var vm = window.ViewModel;
            var model = new LocalModel("audit", "日本語と英語を扱えるローカルモデル・監査用の非常に長いモデル名・Q4_K_M", "audit", "local", "audit", "", 579615840,
                Profile: new("画面監査用", "qwen35", 800000000, 262144, "Q4_K_M", true, 24, 1024, 2, 128, 128, "表示サンプル", ["ja", "en"]));
            vm.Models.Add(model); vm.SelectedModel = model;
            var conversation = new Conversation { Title = "非常に長い会話タイトル：UIとUXの監査で履歴や操作が読めるかを確認する" };
            conversation.Messages.Add(new() { Role = "user", Content = "読みやすさと、操作の分かりやすさを確認してください。" });
            conversation.Messages.Add(new() { Role = "assistant", Mode = "thorough", Content = "# 画面を確認しています\n\nこれは監査用データです。**本文を選択してコピーできます。**\n\n```csharp\nvar longName = \"これは横スクロールと折り返しの確認のための、とても長いコード行です。\";\n```\n\n| 観点 | 状態 |\n| --- | --- |\n| 会話の読みやすさ | 確認中 |\n| キーボード | 確認中 |" });
            for (int i = 0; i < 18; i++) { vm.Conversations.Add(new() { Title = $"監査用の会話 {i + 1}：長い名前でも履歴を探せるか確認する" }); }
            vm.Conversations.Insert(0, conversation); vm.SelectedConversation = conversation;
            await Capture(window, "main-standard");
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            await Capture(window, "main-minimum");
            var messages = (ListBox)window.FindName("MessagesList");
            observations["minimumMessageHeight"] = messages.ActualHeight;

            // Exercise the actual submenu style, independent of saved projects.
            var menu = new ContextMenu { PlacementTarget = (UIElement)window.FindName("ModelSelector") };
            var move = new MenuItem { Header = "プロジェクトへ移動" };
            var destination = new MenuItem { Header = "移動先のプロジェクト" }; move.Items.Add(destination); menu.Items.Add(move);
            menu.IsOpen = true; move.ApplyTemplate(); move.IsSubmenuOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var sub = move.Template.FindName("PART_Popup", move) as Popup;
            bool submenuVisible = sub?.IsOpen == true && destination.IsVisible;
            observations["submenu"] = new { hasPopup = sub is not null, isOpen = sub?.IsOpen == true, destinationVisible = destination.IsVisible };
            // Popup content has its own visual root; save it separately.
            if (sub?.Child is FrameworkElement submenuRoot && submenuRoot.ActualWidth > 0)
            { await SaveVisual(submenuRoot, Path.Combine(output, "project-submenu.png")); }
            menu.IsOpen = false;

            vm.Input = string.Join("\n", Enumerable.Repeat("長文を編集しながら、添付とエラーを確認します。", 10));
            for (int i = 0; i < 6; i++) { conversation.DraftAttachments.Add(new($"添付資料_{i + 1}_長いファイル名の表示を確認します.txt", "document", "監査用")); }
            vm.Error = "モデルを読み込めませんでした。空きメモリを確認し、CPUへの切り替えや小さいモデルを試してください。これは画面監査用のエラーです。";
            await Capture(window, "main-minimum-crowded");
            observations["crowdedMessageHeight"] = messages.ActualHeight;
            conversation.Fork(conversation.Messages.Count, "長い名前の分岐：最小サイズの確認");
            vm.SelectedBranch = conversation.ActiveBranch;
            vm.SelectedWorkMode = WorkModes.Find("debate");
            vm.Inferences.Suspended = true;
            InferenceJob fixture = vm.Inferences.Add(new(Guid.NewGuid(), conversation.Id, conversation.ActiveBranch.Id, Guid.NewGuid(), "表示確認用", DateTimeOffset.UtcNow, model, [model], vm.Settings, "", []), paused: true);
            fixture.State = "active"; fixture.Metrics = "12.4 token/s · 入力 4,096 · 出力 1,024 · 16 GB";
            vm.Inferences.Limit = vm.Inferences.Limit;
            await Capture(window, "main-minimum-all-controls");
            observations["allControlsMessageHeight"] = messages.ActualHeight;
            var input = (TextBox)window.FindName("InputBox");
            var modeButton = (ToggleButton)window.FindName("ModeButton");
            double inputBottom = input.TransformToAncestor(window).Transform(new Point(0, input.ActualHeight)).Y;
            double toolbarTop = modeButton.TransformToAncestor(window).Transform(new Point()).Y;
            double inputHeight = input.ActualHeight;
            var send = Descendants(window).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.SendCommand));
            double sendBottom = send.TransformToAncestor(window).Transform(new Point(0, send.ActualHeight)).Y;
            Border composerBorder = Ancestors(input).OfType<Border>().First();
            double borderBottom = composerBorder.TransformToAncestor(window).Transform(new Point(0, composerBorder.ActualHeight)).Y;
            observations["allControlsInput"] = new { height = inputHeight, inputBottom, toolbarTop, sendBottom, borderBottom };
            observations["composerLayout"] = Ancestors(input).OfType<FrameworkElement>().Select(element => new { type = element.GetType().Name, element.Name, element.ActualHeight, element.ActualWidth, y = element.TransformToAncestor(window).Transform(new Point()).Y, rows = element is Grid grid ? grid.RowDefinitions.Select(r => r.ActualHeight).ToArray() : [] }).ToArray();
            fixture.State = "complete"; fixture.Completion.TrySetResult(); vm.Inferences.Items.Remove(fixture); vm.Inferences.Suspended = false;
            vm.SelectedWorkMode = WorkModes.Find("chat");
            await vm.ApplySettingsAsync(vm.Settings with { Theme = "light" });
            await Capture(window, "main-light-error");
            var errorText = Descendants(window).OfType<TextBox>().First(t => t.Text == vm.Error);
            var errorBorder = Ancestors(errorText).OfType<Border>().First(b => b.Background is SolidColorBrush);
            observations["errorContrast"] = Contrast(((SolidColorBrush)errorText.Foreground).Color, ((SolidColorBrush)errorBorder.Background).Color);
            vm.Input = ""; conversation.DraftAttachments.Clear(); vm.Error = "";
            await vm.ApplySettingsAsync(vm.Settings with { ChatFontSize = 28, LineHeight = 18 });
            await Capture(window, "main-large-font-tight-lines");
            await vm.ApplySettingsAsync(vm.Settings with { ChatFontSize = 15, LineHeight = 25, Theme = "dark" });

            // Escape first closes the thought menu; it must not cancel work behind it.
            bool canceledBehindMenu = false;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task operation = vm.RunMediaOperationAsync(async token =>
            { using CancellationTokenRegistration registration = token.Register(() => canceledBehindMenu = true); await release.Task; });
            var thoughtMenu = (Popup)window.FindName("ModePopup"); thoughtMenu.IsOpen = true;
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            observations["escapeMenu"] = new { closed = !thoughtMenu.IsOpen, canceledBehindMenu };
            release.TrySetResult(); await operation;

            var markdown = new Controls.MarkdownView { Markdown = "Select this text while the response is streaming.\n\n```csharp\nvar value = 42;\n```" };
            var markdownWindow = new Window { Content = markdown, Width = 640, Height = 400, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual };
            markdownWindow.Show(); await Capture(markdownWindow, "markdown-stream-initial");
            var reader = Descendants(markdown).OfType<RichTextBox>().First();
            reader.Selection.Select(reader.Document.ContentStart.GetPositionAtOffset(3)!, reader.Document.ContentStart.GetPositionAtOffset(12)!);
            bool hadSelection = reader.Selection.Text.Length > 0;
            var wrap = Descendants(markdown).OfType<CheckBox>().First(); wrap.IsChecked = false;
            markdown.Markdown += "\n\nOne more token arrived.";
            await Capture(markdownWindow, "markdown-stream-update");
            observations["markdownStreaming"] = new { hadSelection, selectionLost = reader.Selection.Text.Length == 0, wrappingReset = Descendants(markdown).OfType<CheckBox>().First().IsChecked == true };
            markdownWindow.Close();

            // Reproduce retry routing without starting an inference engine.
            vm.Error = "添付ファイルを読み込めませんでした（監査用のエラー）。";
            int branchesBeforeRetry = conversation.Branches.Count, queuedBeforeRetry = vm.Inferences.Items.Count;
            vm.Inferences.Suspended = true;
            try
            {
                vm.RetryCommand.Execute(null);
                for (int i = 0; i < 100 && vm.Inferences.Items.Count == queuedBeforeRetry; i++) { await Task.Delay(20); }
                observations["retryRouting"] = new { attachmentErrorCreatedAnswerBranch = conversation.Branches.Count > branchesBeforeRetry, queuedInference = vm.Inferences.Items.Count > queuedBeforeRetry };
                foreach (InferenceJob queued in vm.Inferences.Items.Skip(queuedBeforeRetry).ToArray()) { vm.Inferences.Cancel(queued); }
            }
            finally { vm.Inferences.Suspended = false; }

            Window[] screens = [new SettingsWindow(vm), new ModelsWindow(store, vm), new DownloadsWindow(vm.Downloads, vm), new ProjectWindow(vm), new SkillsWindow(vm), new InferenceQueueWindow(vm), new MediaWindow(store, vm)];
            foreach (Window screen in screens)
            {
                screen.WindowStartupLocation = WindowStartupLocation.Manual; screen.Left = -20000; screen.Top = -20000;
                screen.Show(); screen.Width = screen.MinWidth; screen.Height = screen.MinHeight;
                string name = screen.GetType().Name;
                await Capture(screen, name + "-minimum");
                if (screen is SettingsWindow settings)
                {
                    foreach (int tab in new[] { 0, 1, 2, 3 })
                    {
                        ((TabControl)settings.FindName("SettingsTabs")).SelectedIndex = tab;
                        if (tab == 1) { await ((Controls.LicensesPanel)settings.FindName("LicenseView")).LoadAsync(); }
                        await Capture(screen, name + "-tab-" + tab);
                    }
                }
                screen.Close();
            }
            observations["fields"] = fields;
            await File.WriteAllTextAsync(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
            if (!submenuVisible) { throw new InvalidOperationException("The project submenu did not render."); }
            if ((double)observations["crowdedMessageHeight"] < 80 || (double)observations["allControlsMessageHeight"] < 80) { throw new InvalidOperationException("The composer hid the conversation at the minimum window size."); }
            if (inputHeight < 36 || inputBottom > toolbarTop + 1 || sendBottom > borderBottom + 1) { throw new InvalidOperationException("The composer hid or overlapped the text input or send button."); }
            if ((double)observations["errorContrast"] < 4.5) { throw new InvalidOperationException("Light-theme error text is unreadable."); }
            if (thoughtMenu.IsOpen || canceledBehindMenu) { throw new InvalidOperationException("Escape canceled work instead of closing the menu."); }
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), "UI audit capture completed using synthetic data; no inference or external search requested.");
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(output, "result.txt"), ex.ToString()); Environment.ExitCode = 1;
        }
        finally { window.Close(); }
    }

    private static async Task SaveVisual(FrameworkElement visual, string path)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        await using var stream = File.Create(path); encoder.Save(stream);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { DependencyObject child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (DependencyObject nested in Descendants(child)) { yield return nested; } }
    }
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject root)
    { for (DependencyObject? item = VisualTreeHelper.GetParent(root); item is not null; item = VisualTreeHelper.GetParent(item)) { yield return item; } }
    private static double Contrast(Color a, Color b)
    {
        static double Luminance(Color c)
        {
            static double Linear(byte v) { double s = v / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }
        double first = Luminance(a), second = Luminance(b); return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
}
