using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace PinList;

/// <summary>
/// Monitor work areas (screen minus taskbar) in WPF units. WinForms reports physical pixels;
/// they are converted with the primary monitor's scale, which is the same scale WPF uses for window positions.
/// </summary>
public static class Screens
{
    static double Scale
    {
        get
        {
            var primary = WinForms.Screen.PrimaryScreen;
            double wpf = SystemParameters.PrimaryScreenWidth;
            return primary == null || wpf <= 0 ? 1 : primary.Bounds.Width / wpf;
        }
    }

    static Rect ToWpf(System.Drawing.Rectangle r, double s) => new(r.X / s, r.Y / s, r.Width / s, r.Height / s);

    public static IReadOnlyList<Rect> WorkAreas()
    {
        double s = Scale;
        return WinForms.Screen.AllScreens.Select(sc => ToWpf(sc.WorkingArea, s)).ToList();
    }

    public static Rect Primary => SystemParameters.WorkArea;

    /// <summary>Work area of the monitor that contains the point (WPF units), or the nearest one.</summary>
    public static Rect WorkAreaAt(Point p)
    {
        double s = Scale;
        var screen = WinForms.Screen.FromPoint(new System.Drawing.Point((int)(p.X * s), (int)(p.Y * s)));
        return ToWpf(screen.WorkingArea, s);
    }

    /// <summary>Work area of the monitor under the mouse cursor.</summary>
    public static Rect WorkAreaAtCursor()
    {
        double s = Scale;
        return ToWpf(WinForms.Screen.FromPoint(WinForms.Cursor.Position).WorkingArea, s);
    }

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
            var visible = Rect.Intersect(area, header);
            if (!visible.IsEmpty && visible.Width >= 60 && visible.Height >= 20) return true;
        }
        return false;
    }
}
