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
        string[] colors = light ? ["#FAFAFA", "#F0F0F0", "#E8E8E8", "#DEDEDE", "#CBCBCB", "#202020", "#656565", "#286B48", "#FFFFFF", "#6843A0", "#79550E", "#36733A", "#23667C"]
            : ["#212121", "#171717", "#303030", "#3B3B3B", "#424242", "#ECECEC", "#A5A5A5", "#B3EBCB", "#10291B", "#9E9BEF", "#C2A76F", "#86AA82", "#89BCD8"];
        for (int i = 0; i < names.Length; i++) { Application.Current.Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])); }
        Application.Current.Resources["ChatBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#FFFFFF" : "#313338"));
        Application.Current.Resources["ChatHoverBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#F2F3F5" : "#2E3035"));
        Application.Current.Resources["ChatInputBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#EBEDEF" : "#383A40"));
        Application.Current.Resources["ErrorBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#FCE8E6" : "#3A2D2D"));
        if (highContrastForAudit ?? SystemParameters.HighContrast)
        {
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
