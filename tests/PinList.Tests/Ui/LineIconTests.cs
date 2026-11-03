using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Xunit;

namespace PinList.Tests;

public class LineIconTests : UiTest
{
    static void RenderOnce(Control c)
    {
        var host = new Window { Width = 40, Height = 40, Content = c };
        host.Show();
        Pump();
        using var bitmap = new RenderTargetBitmap(new PixelSize(40, 40));
        bitmap.Render(host);
        host.Content = null; // the icon can be shown again in another test window
        host.Close();
    }

    [AvaloniaFact]
    public void Icons_are_drawn_in_the_inherited_foreground_color()
    {
        var icon = new LineIcon { Data = (Geometry)App.FindResource("Line.Archive")!, Width = 20, Height = 20 };
        icon.SetValue(TextElement.ForegroundProperty, Brushes.Red);
        RenderOnce(icon);
        Assert.Equal(1.7, icon.StrokeThickness);
        icon.StrokeThickness = 2.4;
        RenderOnce(icon);
        Assert.Equal(2.4, icon.StrokeThickness);
    }

    [AvaloniaFact]
    public void Icons_without_a_shape_a_color_or_a_size_draw_nothing()
    {
        RenderOnce(new LineIcon()); // no data
        var noColor = new LineIcon { Data = (Geometry)App.FindResource("Line.Add")! };
        noColor.SetValue(TextElement.ForegroundProperty, null);
        RenderOnce(noColor);
        var empty = new LineIcon { Data = (Geometry)App.FindResource("Line.Add")!, Width = 0, Height = 0 };
        empty.SetValue(TextElement.ForegroundProperty, Brushes.Red);
        RenderOnce(empty);
    }

    [AvaloniaFact]
    public void Every_line_icon_exists()
    {
        foreach (var key in new[] { "Archive", "Settings", "Grid", "Copy", "Delete", "RemoveFromDesktop", "Transparent", "Sync", "Rename",
                                    "Lock", "Unlock", "Calendar", "Bell", "NewCategory", "More", "MoreVertical", "Back", "Search", "Add" })
            Assert.IsAssignableFrom<Geometry>(App.FindResource("Line." + key));
    }
}
