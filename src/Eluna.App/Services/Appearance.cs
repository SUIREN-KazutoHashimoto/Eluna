using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;

namespace Eluna.App.Services;

public static class Appearance
{
    private sealed class FontValue(double size) { public double Size { get; } = size; }
    private static readonly ConditionalWeakTable<DependencyObject, FontValue> Fonts = new();
    private static double _scale = 1;
    static Appearance()
    {
        EventManager.RegisterClassHandler(typeof(Control), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => ScaleElement((DependencyObject)sender)));
        EventManager.RegisterClassHandler(typeof(TextBlock), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => ScaleElement((DependencyObject)sender)));
        SystemParameters.StaticPropertyChanged += (_, e) => { if (e.PropertyName == nameof(SystemParameters.HighContrast) && Application.Current is { } app) { app.Dispatcher.InvokeAsync(() => Apply(app.Resources["Theme"] as string ?? "dark", _scale)); } };
    }
    public static void Apply(string theme, double scale = 1, bool? highContrastForAudit = null)
    {
        _scale = Math.Clamp(double.IsFinite(scale) ? scale : 1, 1, 1.5);
        Application.Current.Resources["Theme"] = theme;
        bool light = theme == "light";
        string[] names = ["BackgroundBrush", "SidebarBrush", "SurfaceBrush", "HoverBrush", "StrokeBrush", "TextBrush", "MutedBrush", "AccentBrush", "OnAccentBrush", "CodeKeywordBrush", "CodeStringBrush", "CodeCommentBrush", "CodeNumberBrush"];
        string[] colors = light ? ["#E3E5E8", "#F2F3F5", "#EBEDEF", "#DFE1E5", "#D9DCE1", "#313338", "#5C5E66", "#5865F2", "#FFFFFF", "#6843A0", "#79550E", "#36733A", "#23667C"]
            : ["#1E1F22", "#2B2D31", "#232428", "#404249", "#26272D", "#F2F3F5", "#B5BAC1", "#5865F2", "#FFFFFF", "#B0ADF6", "#D1B47B", "#96BA92", "#99CCE8"];
        for (int i = 0; i < names.Length; i++) { Application.Current.Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])); }
        Application.Current.Resources["ChatBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#FFFFFF" : "#313338"));
        Application.Current.Resources["ChatHoverBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#F2F3F5" : "#2E3035"));
        Application.Current.Resources["ChatInputBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#EBEDEF" : "#383A40"));
        Application.Current.Resources["ErrorBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#FCE8E6" : "#3A2D2D"));
        Application.Current.Resources["MentionTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#4338A8" : "#DEE0FF"));
        Application.Current.Resources["MentionBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#E8E7FF" : "#4B4B77"));
        if (highContrastForAudit ?? SystemParameters.HighContrast)
        {
            Application.Current.Resources["MentionTextBrush"] = SystemColors.HighlightTextBrush; Application.Current.Resources["MentionBackgroundBrush"] = SystemColors.HighlightBrush;
            foreach (string key in new[] { "BackgroundBrush", "SidebarBrush", "SurfaceBrush", "ChatBackgroundBrush", "ChatInputBrush", "ErrorBackgroundBrush" }) { Application.Current.Resources[key] = SystemColors.WindowBrush; }
            foreach (string key in new[] { "TextBrush", "MutedBrush", "StrokeBrush", "CodeKeywordBrush", "CodeStringBrush", "CodeCommentBrush", "CodeNumberBrush" }) { Application.Current.Resources[key] = SystemColors.WindowTextBrush; }
            foreach (string key in new[] { "AccentBrush", "HoverBrush", "ChatHoverBrush" }) { Application.Current.Resources[key] = SystemColors.HighlightBrush; }
            Application.Current.Resources["OnAccentBrush"] = SystemColors.HighlightTextBrush;
        }
        foreach (Window window in Application.Current.Windows) { SetTitle(window, !light); ScaleFonts(window); }
    }
    public static void Attach(Window window)
    {
        if (window.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue) { window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window)); }
        window.SourceInitialized += (_, _) => SetTitle(window, !Equals(Application.Current.Resources["Theme"], "light"));
        window.Loaded += (_, _) => ScaleFonts(window);
    }
    private static void ScaleFonts(Window window)
    {
        window.FontSize = 13 * _scale;
        void Visit(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                ScaleElement(child);
                Visit(child);
            }
        }
        Visit(window);
    }
    private static void ScaleElement(DependencyObject element)
    {
        if (element is Window) { return; }
        DependencyProperty? font = element is Control ? Control.FontSizeProperty : element is TextBlock ? TextBlock.FontSizeProperty : null;
        if (font is not null && element.ReadLocalValue(font) is double size && !BindingOperations.IsDataBound(element, font))
        { FontValue original = Fonts.GetValue(element, _ => new(size)); element.SetValue(font, original.Size * _scale); }
    }
    public static void SetTitle(Window window, bool dark)
    {
        int value = dark ? 1 : 0;
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero) { DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)); }
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
