using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace PinList;

/// <summary>
/// Monitor work areas (screen minus taskbar) in "widget units": device-independent pixels at the primary
/// monitor's scale. data.json stores widget positions in these units (the WPF app did too), so existing
/// positions stay where they were.
/// </summary>
public static class Displays
{
    /// <summary>Pixels per widget unit.</summary>
    public static double Scale => ScaleSource();

    /// <summary>Where the scale comes from. Tests replace it.</summary>
#if WINDOWS
    internal static Func<double> ScaleSource { get; set; } = Native.SystemScale;
#else
    internal static Func<double> ScaleSource { get; set; } = PrimaryScaling;

    static double PrimaryScaling() => Source() is { Primary: { } primary } ? primary.Scaling : 1.0;
#endif

    /// <summary>A window to ask for the screens (macOS: any PinList window knows them). Set by each widget.</summary>
    internal static Avalonia.Controls.TopLevel? Anchor { get; set; }

    public static PixelPoint ToPixels(double x, double y) =>
        new((int)Math.Round(x * Scale), (int)Math.Round(y * Scale));

    public static Point FromPixels(PixelPoint p) => new(p.X / Scale, p.Y / Scale);

#if WINDOWS
    static Rect ToUnits(System.Drawing.Rectangle r, double s) => new(r.X / s, r.Y / s, r.Width / s, r.Height / s);

    public static IReadOnlyList<Rect> WorkAreas()
    {
        double s = Scale;
        return System.Windows.Forms.Screen.AllScreens.Select(sc => ToUnits(sc.WorkingArea, s)).ToList();
    }

    public static Rect Primary => ToUnits(System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea, Scale);

    /// <summary>Work area of the monitor that contains the point (widget units), or the nearest one.</summary>
    public static Rect WorkAreaAt(Point p)
    {
        double s = Scale;
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)(p.X * s), (int)(p.Y * s)));
        return ToUnits(screen.WorkingArea, s);
    }

    /// <summary>Work area of the monitor under the mouse cursor.</summary>
    public static Rect WorkAreaAtCursor() =>
        ToUnits(System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea, Scale);
#else
    /// <summary>The screens as Avalonia sees them (any open window knows them). Set by the app.</summary>
    internal static Func<Avalonia.Controls.Screens?> Source { get; set; } = () => Anchor?.Screens;

    static Rect ToUnits(PixelRect r, double s) => new(r.X / s, r.Y / s, r.Width / s, r.Height / s);

    static readonly Rect Fallback = new(0, 0, 1280, 800);

    public static IReadOnlyList<Rect> WorkAreas()
    {
        var all = Source()?.All;
        return all == null || all.Count == 0 ? new[] { Fallback } : all.Select(sc => ToUnits(sc.WorkingArea, Scale)).ToList();
    }

    public static Rect Primary => Source()?.Primary is { } p ? ToUnits(p.WorkingArea, Scale) : Fallback;

    public static Rect WorkAreaAt(Point p) =>
        Source()?.ScreenFromPoint(ToPixels(p.X, p.Y)) is { } sc ? ToUnits(sc.WorkingArea, Scale) : Primary;

    public static Rect WorkAreaAtCursor() => Primary;
#endif

    /// <summary>The same area for PinList.Core.</summary>
    internal static WorkArea ToWorkArea(this Rect r) => new(r.X, r.Y, r.Width, r.Height);

    /// <summary>
    /// True if enough of the widget's header lies on ONE monitor to grab it with the mouse.
    /// (Checking against the rectangle around all monitors isn't enough: with monitors of different
    /// sizes that rectangle has corners no monitor covers.)
    /// </summary>
    public static bool IsReachable(Rect widget, IEnumerable<Rect> workAreas)
    {
        var header = new Rect(widget.X, widget.Y, Math.Max(1, widget.Width), 30);
        foreach (var area in workAreas)
        {
            var visible = area.Intersect(header);
            if (visible.Width >= 60 && visible.Height >= 20) return true;
        }
        return false;
    }
}
