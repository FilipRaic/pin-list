using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace PinList.Tests;

/// <summary>
/// Getting to PinList on a Mac: a Dock icon only while the PinList window is open, and opening PinList again
/// (Finder, Launchpad, Spotlight, a Dock click) shows something. The macOS calls are replaced by a recorder.
/// </summary>
public class MacAccessTests : UiTest
{
    readonly List<bool> _dock = new();
    readonly Action<bool> _before = App.ShowInDock;

    public MacAccessTests() => App.ShowInDock = _dock.Add;

    public override void Dispose()
    {
        base.Dispose();
        App.ShowInDock = _before;
    }

    [AvaloniaFact]
    public void Opening_the_PinList_window_adds_the_Dock_icon_and_closing_removes_it()
    {
        App.ShowListsWindow();
        Pump();
        Assert.True(App.InDock);

        App.ShowSettings(); // another section of the same window: no second call
        Pump();
        App.MainWindow!.Close(); // the close button only hides it
        Pump();
        Assert.False(App.InDock);
        Assert.Equal(new[] { true, false }, _dock);

        App.ShowArchive(); // shown again
        Pump();
        Assert.True(App.InDock);
    }

    [AvaloniaFact]
    public void Widgets_alone_never_add_the_Dock_icon()
    {
        Open(MakeBoard(("A", new string[0])));
        Assert.False(App.InDock);
        Assert.Empty(_dock);
    }

    [AvaloniaFact]
    public void Reopen_with_the_window_hidden_brings_the_widgets_forward_without_a_Dock_icon()
    {
        var w = Open(MakeBoard(("A", new string[0])));
        w.Hide();
        App.OnReopen();
        Pump();
        Assert.True(w.IsVisible);
        Assert.False(App.InDock);
        Assert.Null(App.MainWindow);
    }

    [AvaloniaFact]
    public void Reopen_with_no_widgets_opens_All_lists()
    {
        App.OnReopen();
        Pump();
        Assert.True(App.MainWindow!.IsVisible);
        Assert.Equal(MainSection.Lists, App.MainWindow.Section);
        Assert.True(App.InDock);
    }

    [AvaloniaFact]
    public void Reopen_while_the_window_is_open_keeps_its_section_and_brings_it_forward()
    {
        Open(MakeBoard(("A", new string[0]))); // widgets too: the window still wins
        App.ShowArchive();
        Pump();
        App.OnReopen();
        Pump();
        Assert.Equal(MainSection.Archive, App.MainWindow!.Section);
        Assert.True(App.MainWindow.IsActive);
    }

    [AvaloniaFact]
    public void The_app_menu_has_Settings_All_lists_and_Archive()
    {
        var entries = App.AppMenuEntries();
        Assert.Equal(new[] { "Settings", "All lists", "Archive" }, entries.Select(e => e.Text));
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), entries[0].Gesture);
        Assert.Null(entries[1].Gesture);

        entries[2].Action!();
        Pump();
        Assert.Equal(MainSection.Archive, App.MainWindow!.Section);
        entries[0].Action!();
        Pump();
        Assert.Equal(MainSection.Settings, App.MainWindow.Section);
        entries[1].Action!();
        Pump();
        Assert.Equal(MainSection.Lists, App.MainWindow.Section);
    }

    [AvaloniaFact]
    public void A_start_with_no_widgets_applies_the_Dock_icon_again_after_launch()
    {
        // On a Mac, Avalonia sets "no Dock icon" when launching finishes, after Start opened the window
        App.Stop();
        File.WriteAllText(Path.Combine(UiHost.DataDir, "data.json"), """{ "version": 1, "boards": [] }""");
        Tray.Disposed = false;
        Assert.True(App.Start());
        Pump();
        Assert.True(App.InDock);
        Assert.Equal(new[] { true, true }, _dock);
        Store.UseDirectory(Dir);
    }

    [AvaloniaFact]
    public void Reopen_while_the_window_is_open_leaves_focus_and_announcements_alone()
    {
        App.ShowListsWindow(); // no lists: New list is focused and the empty text announced once
        Pump();
        A11y.LastAnnouncement = null;
        App.OnReopen();
        Pump();
        Assert.Null(A11y.LastAnnouncement);
        Assert.True(App.MainWindow!.IsActive);
    }

    [AvaloniaFact]
    public void Exit_with_the_window_open_removes_the_Dock_icon()
    {
        App.ShowListsWindow();
        Pump();
        App.CloseMainWindow();
        Pump();
        Assert.False(App.InDock);
        Assert.Equal(new[] { true, false }, _dock);
    }
}
