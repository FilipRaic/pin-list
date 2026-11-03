using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace PinList.Tests;

public class CollapserTests
{
    static (Collapser, Border) Make(double reveal)
    {
        var child = new Border { Height = 40, Width = 100 };
        var c = new Collapser { Child = child, Reveal = reveal };
        c.Measure(new Size(200, 200));
        return (c, child);
    }

    [AvaloniaFact]
    public void Reveal_scales_the_height()
    {
        Assert.Equal(40, Make(1).Item1.DesiredSize.Height);
        Assert.Equal(20, Make(0.5).Item1.DesiredSize.Height);
        Assert.Equal(0, Make(0).Item1.DesiredSize.Height);
        Assert.Equal(100, Make(0.5).Item1.DesiredSize.Width);
    }

    [AvaloniaFact]
    public void Reveal_stays_between_0_and_1()
    {
        var (c, _) = Make(1);
        c.Reveal = 3;
        Assert.Equal(1, c.Reveal);
        c.Reveal = -1;
        Assert.Equal(0, c.Reveal);
    }

    [AvaloniaFact]
    public void The_child_keeps_its_height_and_is_clipped()
    {
        var (c, child) = Make(0.25);
        c.Arrange(new Rect(0, 0, 100, c.DesiredSize.Height));
        Assert.True(c.ClipToBounds);
        Assert.Equal(10, c.Bounds.Height);
        Assert.Equal(40, child.Bounds.Height);
    }

    [AvaloniaFact]
    public void Without_a_child_it_has_no_size()
    {
        var c = new Collapser();
        c.Measure(new Size(100, 100));
        Assert.Equal(new Size(0, 0), c.DesiredSize);
    }

    [AvaloniaFact]
    public void Arranging_without_a_child_does_nothing()
    {
        var c = new Collapser();
        c.Measure(new Size(100, 100));
        c.Arrange(new Rect(0, 0, 100, 0));
        Assert.Equal(0, c.Bounds.Height);
    }
}
