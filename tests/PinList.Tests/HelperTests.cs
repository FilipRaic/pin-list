using Avalonia.Headless.XUnit;
using Avalonia;
using PinList;
using Xunit;

namespace PinList.Tests;

public class DisplaysTests
{
    // A big monitor on the left and a smaller one on the right, aligned at the top
    static readonly Rect[] Monitors = { new(0, 0, 2560, 1400), new(2560, 0, 1920, 1040) };

    [AvaloniaFact]
    public void A_widget_on_a_monitor_is_reachable() =>
        Assert.True(Displays.IsReachable(new Rect(100, 100, 320, 480), Monitors));

    [AvaloniaFact]
    public void A_widget_in_the_corner_no_monitor_covers_is_not_reachable() =>
        // inside the rectangle around both monitors, but below the smaller one
        Assert.False(Displays.IsReachable(new Rect(3000, 1200, 320, 480), Monitors));

    [AvaloniaFact]
    public void A_widget_mostly_off_screen_needs_60_visible_pixels_of_header()
    {
        Assert.True(Displays.IsReachable(new Rect(-250, 10, 320, 480), Monitors));  // 70 px visible
        Assert.False(Displays.IsReachable(new Rect(-280, 10, 320, 480), Monitors)); // 40 px visible
    }

    [AvaloniaFact]
    public void A_widget_whose_header_is_below_the_work_area_is_not_reachable() =>
        Assert.False(Displays.IsReachable(new Rect(100, 1395, 320, 480), Monitors));
}
