using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Eluna.App.Models;

namespace Eluna.App.Controls;

public partial class ComputerTaskPanel : UserControl
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(nameof(Session), typeof(ComputerSession), typeof(ComputerTaskPanel), new PropertyMetadata(null, SessionChanged));
    public ComputerSession? Session { get => (ComputerSession?)GetValue(SessionProperty); set => SetValue(SessionProperty, value); }
    public ComputerTaskPanel() { InitializeComponent(); Visibility = Visibility.Collapsed; }
    private static void SessionChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        var panel = (ComputerTaskPanel)owner;
        if (e.OldValue is ComputerSession old) { PropertyChangedEventManager.RemoveHandler(old, panel.Changed, ""); }
        if (e.NewValue is ComputerSession next) { PropertyChangedEventManager.AddHandler(next, panel.Changed, ""); }
        panel.Card.DataContext = e.NewValue; panel.Visibility = e.NewValue is null ? Visibility.Collapsed : Visibility.Visible; panel.Draw();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ComputerSession.Preview) or nameof(ComputerSession.Review)) { Draw(); }
        if (e.PropertyName == nameof(ComputerSession.Stage)) { RevealApproval(); }
    }
    private void Command_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string command }) { Session?.Invoke(command); } }
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) { Draw(); RevealApproval(); }
    private void RevealApproval()
    { if (Session?.Reviewing == true && IsVisible) { Dispatcher.InvokeAsync(() => { if (Session?.Reviewing == true && IsVisible) { ApproveButton.BringIntoView(); } }, System.Windows.Threading.DispatcherPriority.Loaded); } }
    private void Draw()
    {
        if (Markers is null) { return; }
        Markers.Children.Clear();
        if (Session?.Review is not { } review || Session.Observation is not { } observation) { return; }
        if (review.Action.Type is not ("click" or "double_click" or "right_click" or "drag" or "scroll")) { return; }
        double scale = Math.Min(Viewport.ActualWidth / observation.Bounds.Width, Viewport.ActualHeight / observation.Bounds.Height);
        double left = (Viewport.ActualWidth - observation.Bounds.Width * scale) / 2, top = (Viewport.ActualHeight - observation.Bounds.Height * scale) / 2;
        ComputerElement? element = observation.Elements.FirstOrDefault(e => e.Id == review.Action.Element);
        double x = element is null ? observation.Bounds.Width * review.Action.X / 1000d : element.Bounds.Left - observation.Bounds.Left + element.Bounds.Width / 2d;
        double y = element is null ? observation.Bounds.Height * review.Action.Y / 1000d : element.Bounds.Top - observation.Bounds.Top + element.Bounds.Height / 2d;
        var marker = new Ellipse { Width = 24, Height = 24, Stroke = Brushes.Orange, StrokeThickness = 3 }; Canvas.SetLeft(marker, left + x * scale - 12); Canvas.SetTop(marker, top + y * scale - 12); Markers.Children.Add(marker);
        if (review.Action.Type == "drag") { Markers.Children.Add(new Line { X1 = left + x * scale, Y1 = top + y * scale, X2 = left + observation.Bounds.Width * review.Action.X2 / 1000d * scale, Y2 = top + observation.Bounds.Height * review.Action.Y2 / 1000d * scale, Stroke = Brushes.Orange, StrokeThickness = 2 }); }
    }
}
