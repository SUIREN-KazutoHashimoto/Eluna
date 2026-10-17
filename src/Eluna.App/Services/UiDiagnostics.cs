using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Eluna.App.Models;

namespace Eluna.App.Services;

internal static class UiDiagnostics
{
    private static readonly ConditionalWeakTable<Window, AppTrace> Roots = new();
    static UiDiagnostics()
    {
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler((sender, _) => Record(sender, "click")), true);
        EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.ClickEvent, new RoutedEventHandler((sender, _) => Record(sender, "menu")), true);
        EventManager.RegisterClassHandler(typeof(Selector), Selector.SelectionChangedEvent, new SelectionChangedEventHandler((sender, _) => Record(sender, "selection")), true);
        EventManager.RegisterClassHandler(typeof(TextBox), TextBoxBase.TextChangedEvent, new TextChangedEventHandler((sender, _) =>
        {
            if (sender is not TextBox { IsReadOnly: false } text || Resolve(text) is not { Detailed: true } trace) { return; }
            // UI fields contribute metadata only. Their values are never copied to diagnostics.
            trace.Write("debug", "ui", "text.changed", Name(text) + "; characters=" + text.Text.Length);
        }), true);
    }
    internal static void Attach(Window window, AppTrace trace) { Roots.Remove(window); Roots.Add(window, trace); }
    private static AppTrace? Resolve(DependencyObject source)
    {
        Window? window = Window.GetWindow(source); while (window?.Owner is { } owner) { window = owner; }
        return window is not null && Roots.TryGetValue(window, out AppTrace? trace) ? trace : null;
    }
    private static string Name(FrameworkElement element)
    {
        string target = element.DataContext switch { AgentAccount a => "; account=" + a.Id, Conversation c => "; room=" + c.Id, ChatMessage m => "; message=" + m.Id, _ => "" };
        return element.GetType().Name + "; id=" + element.Name + target;
    }
    private static void Record(object sender, string kind)
    { if (sender is FrameworkElement element && Resolve(element) is { Detailed: true } trace) { trace.Write("debug", "ui", kind, Name(element)); } }
}
