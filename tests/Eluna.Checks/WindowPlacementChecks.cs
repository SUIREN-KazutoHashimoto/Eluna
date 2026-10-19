using Eluna.App.Services;

namespace Eluna.Checks;

internal static class WindowPlacementChecks
{
    internal static void Run()
    {
        static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
        static WindowRestorePlan Plan(WindowPlacement saved, params WindowDisplay[] displays) => WindowPlacement.Plan(saved, displays, 820, 600) ?? throw new InvalidOperationException("No window restore plan.");
        var primary = new WindowDisplay("primary-id", "DISPLAY1", new(0, 0, 1920, 1080), new(0, 40, 1920, 1040), 96, true);
        var left = new WindowDisplay("left-id", "DISPLAY2", new(-2560, -300, 2560, 1440), new(-2560, -260, 2560, 1400), 144, false);
        var saved = new WindowPlacement(-2400, -140, 1350, 900, false, 2, left.Id, left.Device, left.WorkArea, 144);
        Check(Plan(saved, primary, left).Bounds == saved.Bounds, "Secondary monitor position was redirected or changed.");
        var moved = left with { Device = "DISPLAY3", Bounds = new(2000, 0, 3840, 2160), WorkArea = new(2000, 48, 3840, 2112), Dpi = 192 };
        WindowRestorePlan relocated = Plan(saved, primary, moved);
        Check(relocated.Display == moved && relocated.Bounds == new WindowArea(2213, 208, 1800, 1200), "Monitor identity, local offset or logical size was lost after DPI/layout change.");
        WindowRestorePlan disconnected = Plan(saved, primary);
        Check(disconnected.Display == primary && disconnected.Bounds == new WindowArea(107, 120, 900, 600), "Disconnected monitor did not restore to the available work area.");
        var renamed = primary with { Device = left.Device };
        Check(Plan(saved, renamed, moved).Display == moved, "A reused DISPLAY number overrode the physical monitor identity.");
        var far = left with { Bounds = new(-16000, -4000, 2560, 1440), WorkArea = new(-16000, -3960, 2560, 1400) };
        Check(Plan(saved, far).Bounds == new WindowArea(-15840, -3840, 1350, 900), "Valid large negative desktop coordinates were rejected.");
        var tiny = primary with { Bounds = new(0, 0, 800, 600), WorkArea = new(40, 40, 760, 560), Dpi = 192 };
        Check(Plan(saved with { Width = 5000, Height = 5000 }, tiny).Bounds == tiny.WorkArea, "A small high-DPI desktop left the title bar/window outside the work area.");
        Check(Plan(new(-2300, -200, 1000, 700, true), primary, left) is { Display.Id: "left-id", Maximized: true }, "Legacy negative-position data did not select the secondary monitor or keep maximization.");
        Check(Plan(new(-2300, -200, 1000, 700, true), primary, left).Bounds.Width == 1500, "Legacy DIP size was not scaled for the target monitor.");
        WindowArea workspace = new(-2400, -180, 1350, 900);
        Check(WindowNative.ScreenBounds(workspace, left) == saved.Bounds && WindowNative.WorkspaceBounds(saved.Bounds, left) == workspace, "Top taskbar workspace coordinates drifted.");
        var sideTaskbar = left with { WorkArea = new(-2510, -300, 2510, 1440) };
        Check(MaximizedWorkArea.RelativeArea(primary) == new WindowArea(0, 40, 1920, 1040) && MaximizedWorkArea.RelativeArea(left) == new WindowArea(0, 40, 2560, 1400), "Top-taskbar maximization mixed physical coordinates with DIPs.");
        Check(MaximizedWorkArea.RelativeArea(sideTaskbar) == new WindowArea(50, 0, 2510, 1440), "A side taskbar or negative monitor origin changed the maximized work area.");
        Check(MaximizedWorkArea.RelativeArea(moved with { WorkArea = new(2000, 0, 3840, 2112) }) == new WindowArea(0, 0, 3840, 2112), "Bottom-taskbar maximization used the monitor's full height.");
        Check(WindowNative.ScreenBounds(workspace, sideTaskbar) == workspace with { Left = -2350 }, "Side taskbar offset was lost.");
        Check(WindowNative.ScreenBounds(workspace, left, true) == workspace && WindowNative.WorkspaceBounds(workspace, left, true) == workspace, "Tool-window screen coordinates were shifted.");
        foreach (WindowPlacement invalid in new[] { saved with { Width = -1 }, saved with { Height = 0 }, saved with { Left = double.NaN }, saved with { Top = double.PositiveInfinity }, saved with { Dpi = 0 }, saved with { Version = 99 }, saved with { WorkArea = null }, saved with { Left = double.MaxValue } })
        { Check(WindowPlacement.Plan(invalid, [primary, left], 820, 600) is null, "Invalid placement was accepted."); }
        Check(WindowPlacement.Plan(saved, [], 820, 600) is null, "An empty display list was not handled.");
        var random = new Random(710);
        foreach (int dpi in new[] { 96, 120, 144, 168, 192, 240, 288 })
        {
            WindowDisplay target = moved with { Dpi = dpi };
            for (int i = 0; i < 100; i++)
            {
                WindowPlacement current = saved with { Left = random.Next(-12000, 12000), Top = random.Next(-12000, 12000), Width = random.Next(1, 8000), Height = random.Next(1, 8000) };
                WindowArea bounds = Plan(current, target).Bounds;
                Check(bounds.Left >= target.WorkArea.Left && bounds.Top >= target.WorkArea.Top && bounds.Right <= target.WorkArea.Right && bounds.Bottom <= target.WorkArea.Bottom, "Restored window escaped the work area.");
                WindowPlacement roundtrip = new(bounds.Left, bounds.Top, bounds.Width, bounds.Height, false, 2, target.Id, target.Device, target.WorkArea, dpi);
                Check(Plan(roundtrip, target).Bounds == bounds, "Repeated restoration accumulated position/size drift.");
            }
        }
        Console.WriteLine("Window placement: identity, mixed-DPI/layout changes, disconnect fallback, negative coordinates, legacy migration, taskbars, invalid data and 700 no-drift roundtrips: OK");
    }
}
