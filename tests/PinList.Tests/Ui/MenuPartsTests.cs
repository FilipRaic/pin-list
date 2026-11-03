using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace PinList.Tests;

/// <summary>The building blocks of the new menus: icons, switches, danger entries and the swatch row.</summary>
public class MenuPartsTests : UiTest
{
    static readonly (string Name, string Hex)[] Three = { ("Red", "#FF0000"), ("Green", "#00FF00"), ("Blue", "#0000FF") };

    ContextMenu OpenMenu(params Avalonia.Controls.MenuItem[] items)
    {
        var w = OpenNew(("A", new[] { "a" }));
        var menu = new ContextMenu();
        foreach (var item in items) menu.Items.Add(item);
        menu.Open(w);
        Pump();
        return menu;
    }

    [AvaloniaFact]
    public void Swatch_row_works_with_the_keyboard()
    {
        string? picked = null;
        var swatch = new SwatchMenuItem("Color", Three, "#FF0000", hex => picked = hex);
        OpenMenu(swatch);
        Assert.Equal("Color, Red, 1 of 3", AutomationProperties.GetName(swatch));

        Press(swatch, Key.Right);
        Press(swatch, Key.Right);
        Press(swatch, Key.Right); // stays on the last one
        Assert.Equal("#0000FF", swatch.HighlightedHex);
        Assert.Contains("Blue", AutomationProperties.GetName(swatch));
        Assert.Equal("Color, Blue, 3 of 3", AutomationProperties.GetName(swatch));
        Press(swatch, Key.Left);
        Assert.Equal("Color, Green, 2 of 3", AutomationProperties.GetName(swatch));
        Press(swatch, Key.Right);
        Assert.Null(picked);

        Press(swatch, Key.Enter);
        Assert.Equal("#0000FF", picked);
    }

    [AvaloniaFact]
    public void Space_picks_too_and_the_current_color_starts_highlighted()
    {
        string? picked = null;
        var swatch = new SwatchMenuItem("Color", Three, "#00ff00", hex => picked = hex); // any letter case
        OpenMenu(swatch);
        Assert.Equal("#00FF00", swatch.HighlightedHex);
        Press(swatch, Key.Space);
        Assert.Equal("#00FF00", picked);
    }

    [AvaloniaFact]
    public void An_unknown_current_color_starts_on_the_first()
    {
        var swatch = new SwatchMenuItem("Color", Three, "#123456", _ => { });
        Assert.Equal("#FF0000", swatch.HighlightedHex);
    }

    [AvaloniaFact]
    public void Clicking_a_swatch_picks_it()
    {
        string? picked = null;
        var swatch = new SwatchMenuItem("Color", Three, "#FF0000", hex => picked = hex);
        OpenMenu(swatch);
        var rings = FindAll<Border>(swatch).Where(b => b.Width == 24).ToList();
        Assert.Equal(3, rings.Count);
        MouseDown(rings[1]);
        Assert.Equal("#00FF00", swatch.HighlightedHex);
        swatch.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent, swatch));
        Pump();
        Assert.Equal("#00FF00", picked);
    }

    [AvaloniaFact]
    public void Switch_items_show_their_state()
    {
        int toggles = 0;
        var on = App.CreateSwitch("Transparent", true, () => toggles++, "Line.Transparent");
        var off = App.CreateSwitch("Sync this list", false, () => { });
        Assert.True(on.IsChecked);
        Assert.False(off.IsChecked);
        Assert.Contains("switch", on.Classes);
        Assert.IsType<LineIcon>(on.Icon);
        Assert.Null(off.Icon);
        Assert.Equal("Transparent", AutomationProperties.GetName(on));

        OpenMenu(on, off);
        var track = FindAll<Border>(on).First(b => b.Classes.Contains("switchtrack"));
        Assert.Contains("on", track.Classes);
        Assert.DoesNotContain("on", FindAll<Border>(off).First(b => b.Classes.Contains("switchtrack")).Classes);

        on.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent, on));
        Pump();
        Assert.Equal(1, toggles);
        on.IsChecked = false;
        Assert.DoesNotContain("on", track.Classes);
    }

    [AvaloniaFact]
    public void Menu_items_can_have_icons_and_be_red()
    {
        bool ran = false;
        var plain = App.CreateMenuItem("Copy", () => ran = true, "Ctrl+C", "Line.Copy");
        var delete = App.CreateMenuItem("Delete list", () => { }, icon: "Line.Delete", danger: true);
        var bare = App.CreateMenuItem("Bare", () => { });
        var icon = Assert.IsType<LineIcon>(plain.Icon);
        Assert.Equal(15, icon.Width);
        Assert.Same(App.FindResource("Line.Copy"), icon.Data);
        Assert.Equal("Ctrl+C", AutomationProperties.GetAcceleratorKey(plain));
        Assert.Contains("danger", delete.Classes);
        Assert.DoesNotContain("danger", plain.Classes);
        Assert.Null(bare.Icon);

        OpenMenu(plain, delete, bare);
        plain.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent, plain));
        Pump();
        Assert.True(ran);
    }

    [AvaloniaFact]
    public void The_check_mark_of_switches_and_swatches_is_hidden()
    {
        var swatch = new SwatchMenuItem("Color", Three, "#FF0000", _ => { });
        var sw = App.CreateSwitch("Transparent", true, () => { });
        OpenMenu(swatch, sw);
        foreach (var item in new Avalonia.Controls.MenuItem[] { swatch, sw })
            Assert.False(FindAll<ContentControl>(item).First(c => c.Name == "PART_ToggleIconPresenter").IsVisible);
    }
}
