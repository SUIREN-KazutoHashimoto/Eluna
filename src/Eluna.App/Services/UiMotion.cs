using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Eluna.App.Services;

public static class UiMotion
{
    private static bool _reduced;
    public static bool Enabled => !_reduced && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    internal static event Action? Changed;
    public static readonly DependencyProperty FeedbackProperty = DependencyProperty.RegisterAttached("Feedback", typeof(string), typeof(UiMotion), new PropertyMetadata("", Attach));
    public static string GetFeedback(DependencyObject element) => (string)element.GetValue(FeedbackProperty);
    public static void SetFeedback(DependencyObject element, string value) => element.SetValue(FeedbackProperty, value);
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(MotionState), typeof(UiMotion));
    static UiMotion() { SystemParameters.StaticPropertyChanged += SystemChanged; }
    private static void SystemChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast)) { Application.Current?.Dispatcher.InvokeAsync(() => Changed?.Invoke()); } }
    internal static void Configure(bool reduced) { if (_reduced != reduced) { _reduced = reduced; Changed?.Invoke(); } }
    private static void Attach(DependencyObject d, DependencyPropertyChangedEventArgs e)
    { if (d is FrameworkElement element) { (element.GetValue(StateProperty) as MotionState)?.Dispose(); element.SetValue(StateProperty, new MotionState(element, e.NewValue as string ?? "")); } }
    internal static void Fade(UIElement element, double target, int milliseconds = 120)
    {
        double from = element.Opacity; element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = target;
        if (Enabled && element.IsVisible && Math.Abs(from - target) > .01) { element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop }); }
    }
    internal static void Reveal(UIElement element)
    { if (Enabled && element.IsVisible) { element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.6, 1, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.Stop }); } }
    private sealed class RadiusAnimation : AnimationTimeline
    {
        internal CornerRadius From, To;
        public override Type TargetPropertyType => typeof(CornerRadius);
        protected override Freezable CreateInstanceCore() => new RadiusAnimation { From = From, To = To };
        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        {
            double p = 1 - Math.Pow(1 - (animationClock.CurrentProgress ?? 1), 3);
            return new CornerRadius(From.TopLeft + (To.TopLeft - From.TopLeft) * p, From.TopRight + (To.TopRight - From.TopRight) * p, From.BottomRight + (To.BottomRight - From.BottomRight) * p, From.BottomLeft + (To.BottomLeft - From.BottomLeft) * p);
        }
    }
    private sealed class MotionState : IDisposable
    {
        private readonly FrameworkElement _element;
        private readonly string _kind;
        private FrameworkElement? _owner;
        private Window? _window;
        private ScaleTransform? _scale;
        private Transform? _original;
        private Point _origin;
        private bool _active;
        internal MotionState(FrameworkElement element, string kind)
        { _element = element; _kind = kind; element.Loaded += Loaded; element.Unloaded += Unloaded; if (element.IsLoaded) { Loaded(element, new RoutedEventArgs()); } }
        private void Loaded(object sender, RoutedEventArgs e)
        {
            if (_active || _kind.Length == 0) { return; }
            _active = true; Changed += Reset;
            if (_kind == "press")
            {
                _original = _element.RenderTransform; _origin = _element.RenderTransformOrigin; _scale = new(1, 1); var transforms = new TransformGroup(); transforms.Children.Add(_original); transforms.Children.Add(_scale); _element.RenderTransform = transforms; _element.RenderTransformOrigin = new(.5, .5);
                _element.PreviewMouseLeftButtonDown += Press; _element.PreviewMouseLeftButtonUp += Release; _element.MouseLeave += Leave; _element.LostMouseCapture += Release; _element.IsEnabledChanged += EnabledChanged;
            }
            if (_kind is "server" or "toolbar")
            {
                DependencyObject? parent = _element;
                while ((parent = VisualTreeHelper.GetParent(parent)) is not null) { if (_kind == "server" && parent is ListBoxItem || _kind == "toolbar" && parent is Border { Name: "MessageBorder" }) { _owner = (FrameworkElement)parent; break; } }
                if (_owner is not null) { _owner.MouseEnter += Enter; _owner.MouseLeave += Leave; _owner.GotKeyboardFocus += Focus; _owner.LostKeyboardFocus += Focus; _owner.AddHandler(Selector.SelectedEvent, new RoutedEventHandler(Selection)); _owner.AddHandler(Selector.UnselectedEvent, new RoutedEventHandler(Selection)); }
            }
            if (_kind == "progress")
            {
                DependencyObject? parent = _element; while ((parent = VisualTreeHelper.GetParent(parent)) is not null) { if (parent is ProgressBar bar) { _owner = bar; break; } }
                if (_owner is not null) { _owner.SizeChanged += ProgressSize; }
                _element.IsVisibleChanged += ProgressVisibility; _window = Window.GetWindow(_element);
                if (_window is not null) { _window.Activated += ProgressWindow; _window.Deactivated += ProgressWindow; _window.StateChanged += ProgressWindow; }
            }
            if (_kind == "entrance") { Reveal(_element); } else { Update(false); }
        }
        private void Enter(object sender, MouseEventArgs e) => Update(true);
        private void Leave(object sender, MouseEventArgs e) { if (_kind == "press") { Scale(1); } else { Update(true); } }
        private void Focus(object sender, KeyboardFocusChangedEventArgs e) => Update(true);
        private void Selection(object sender, RoutedEventArgs e) => Update(true);
        private void EnabledChanged(object sender, DependencyPropertyChangedEventArgs e) => Scale(1);
        private void ProgressSize(object sender, SizeChangedEventArgs e) => Update(false);
        private void ProgressVisibility(object sender, DependencyPropertyChangedEventArgs e) => Update(false);
        private void ProgressWindow(object? sender, EventArgs e) => Update(false);
        private void Press(object sender, MouseButtonEventArgs e) => Scale(.96);
        private void Release(object sender, MouseEventArgs e) => Scale(1);
        private void Scale(double value, bool animate = true)
        {
            if (_scale is null) { return; }
            double from = _scale.ScaleX; _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); _scale.ScaleX = _scale.ScaleY = Enabled ? value : 1;
            if (animate && Enabled) { var a = new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(100)) { FillBehavior = FillBehavior.Stop }; _scale.BeginAnimation(ScaleTransform.ScaleXProperty, a); _scale.BeginAnimation(ScaleTransform.ScaleYProperty, a); }
        }
        private void Update(bool animate)
        {
            if (_kind == "progress" && _element.RenderTransform is TranslateTransform travel)
            {
                double width = Math.Max(64, _owner?.ActualWidth ?? 64); travel.BeginAnimation(TranslateTransform.XProperty, null); travel.X = (width - 64) / 2;
                if (_active && Enabled && _element.IsVisible && _window?.IsActive == true && _window.WindowState != WindowState.Minimized) { travel.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-64, width, TimeSpan.FromSeconds(1.8)) { RepeatBehavior = RepeatBehavior.Forever }); }
            }
            if (_kind == "toolbar") { double to = _owner?.IsMouseOver == true || _owner?.IsKeyboardFocusWithin == true ? 1 : 0; _element.IsHitTestVisible = to > 0; if (animate) { Fade(_element, to, 100); } else { _element.BeginAnimation(UIElement.OpacityProperty, null); _element.Opacity = to; } }
            if (_kind == "server" && _element is Border tile && _owner is ListBoxItem item)
            {
                CornerRadius from = tile.CornerRadius, target = new(item.IsSelected || item.IsMouseOver ? 16 : 24); tile.BeginAnimation(Border.CornerRadiusProperty, null); tile.CornerRadius = target;
                if (animate && Enabled) { tile.BeginAnimation(Border.CornerRadiusProperty, new RadiusAnimation { From = from, To = target, Duration = TimeSpan.FromMilliseconds(160), FillBehavior = FillBehavior.Stop }); }
                if (item.Template.FindName("Mark", item) is Border mark)
                {
                    double old = mark.Height, height = item.IsSelected ? 36 : item.IsMouseOver ? 20 : 8; mark.BeginAnimation(FrameworkElement.HeightProperty, null); mark.Height = height; mark.Opacity = item.IsSelected || item.IsMouseOver ? 1 : 0;
                    if (animate && Enabled) { mark.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(old, height, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop }); }
                }
            }
        }
        private void Reset() { Scale(1, false); _element.BeginAnimation(UIElement.OpacityProperty, null); Update(false); }
        private void Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_active) { return; }
            _active = false; Changed -= Reset; Reset();
            _element.PreviewMouseLeftButtonDown -= Press; _element.PreviewMouseLeftButtonUp -= Release; _element.MouseLeave -= Leave; _element.LostMouseCapture -= Release; _element.IsEnabledChanged -= EnabledChanged;
            if (_scale is not null) { _element.RenderTransform = _original ?? Transform.Identity; _element.RenderTransformOrigin = _origin; _scale = null; }
            _element.IsVisibleChanged -= ProgressVisibility;
            if (_window is not null) { _window.Activated -= ProgressWindow; _window.Deactivated -= ProgressWindow; _window.StateChanged -= ProgressWindow; _window = null; }
            if (_owner is not null) { _owner.SizeChanged -= ProgressSize; }
            if (_owner is not null) { _owner.MouseEnter -= Enter; _owner.MouseLeave -= Leave; _owner.GotKeyboardFocus -= Focus; _owner.LostKeyboardFocus -= Focus; _owner.RemoveHandler(Selector.SelectedEvent, new RoutedEventHandler(Selection)); _owner.RemoveHandler(Selector.UnselectedEvent, new RoutedEventHandler(Selection)); _owner = null; }
        }
        public void Dispose() { Unloaded(_element, new RoutedEventArgs()); _element.Loaded -= Loaded; _element.Unloaded -= Unloaded; }
    }
}
