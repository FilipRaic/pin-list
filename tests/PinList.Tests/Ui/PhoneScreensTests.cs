using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace PinList.Tests;

/// <summary>The phone's bottom bar, the new sheets and the motion of the screens.</summary>
public partial class PhoneTests
{
    [AvaloniaFact]
    public void Bottom_bar_switches_sections_and_hides_in_a_list()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        Show();
        Assert.True(_view.BottomBarVisible);
        Assert.Equal(PhoneSection.Lists, _view.Section);
        Assert.Contains("selected", Named<Button>(_view, "TabLists").Classes);

        Click(Named<Button>(_view, "TabArchive"));
        Assert.Equal(PhoneSection.Archive, _view.Section);
        Assert.True(Named<DockPanel>(_view, "ArchiveScreen").IsVisible);
        Assert.False(Named<DockPanel>(_view, "HomeScreen").IsVisible);
        Assert.Contains("selected", Named<Button>(_view, "TabArchive").Classes);
        Assert.DoesNotContain("selected", Named<Button>(_view, "TabLists").Classes);

        Click(Named<Button>(_view, "TabSettings"));
        Assert.Equal(PhoneSection.Settings, _view.Section);
        Assert.True(Named<DockPanel>(_view, "SettingsScreen").IsVisible);
        Assert.True(_view.BottomBarVisible);
        Click(Named<Button>(_view, "TabSettings")); // the page is only made once
        Assert.NotNull(_view.SettingsPage);

        Click(Named<Button>(_view, "TabLists"));
        _view.ShowBoard(b);
        Assert.False(_view.BottomBarVisible);
        Assert.True(_view.GoBack());
        Assert.True(_view.BottomBarVisible);

        _view.ShowBoard(b);
        _view.ShowSection(PhoneSection.Archive); // leaving a list through the code also closes it
        Assert.Null(_view.OpenBoard);
    }

    [AvaloniaFact]
    public void Lists_screen_has_a_title_and_a_labeled_new_list_button()
    {
        Show();
        Assert.Equal("Lists", Named<TextBlock>(_view, "HomeTitle").Text);
        var button = Named<Button>(_view, "AddBoardButton");
        Assert.Equal("New list", NameOf(button));
        Assert.Contains(FindAll<TextBlock>(button), t => t.Text == "New list");
    }

    [AvaloniaFact]
    public void The_list_sheet_has_a_color_row_and_a_sync_switch()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        ShowWithSync();
        PhoneSignIn();
        LongPress(Card(b));
        var swatches = FindAll<Button>(_view.SheetPanel!).Where(x => x.Classes.Contains("swatch")).ToList();
        Assert.Equal(Themes.BoardStyles.Length, swatches.Count);
        Assert.Contains("selected", swatches[0].Classes); // Dark is the current color
        var sync = Assert.IsType<SheetRow>(_view.SheetPanel!.Children.OfType<Button>().First(x => x.Content as string == "Sync this list"));
        Assert.True(sync.HasSwitch);
        Assert.False(sync.SwitchOn);
        ChooseColor("Yellow");
        Assert.Equal("Yellow", b.Style);
        Assert.False(_view.SheetOpen);
    }

    [AvaloniaFact]
    public void Sheet_rows_have_icons()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        Show();
        _view.ShowBoard(b);
        Pump();
        LongPress(PhoneRow(b.Categories[0].Items[0]));
        var rows = _view.SheetPanel!.Children.OfType<SheetRow>().ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.NotNull(r.Icon));
        Assert.Contains("danger", rows.Last().Classes);
    }

    [AvaloniaFact]
    public void Swiping_the_sheet_down_closes_it()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        Show();
        LongPress(Card(b));
        Assert.True(_view.SheetOpen);

        // a short drag springs back
        var handle = _view.SheetHandleArea;
        var p = At(handle);
        _window.MouseDown(p, MouseButton.Left);
        _window.MouseMove(p + new Point(0, 30), RawInputModifiers.LeftMouseButton);
        UiHost.Wait(TimeSpan.FromMilliseconds(400)); // slow: no flick
        _window.MouseUp(p + new Point(0, 30), MouseButton.Left);
        Pump();
        Assert.True(_view.SheetOpen);

        // a long drag closes it
        p = At(handle);
        _window.MouseDown(p, MouseButton.Left);
        _window.MouseMove(p + new Point(0, 120), RawInputModifiers.LeftMouseButton);
        _window.MouseUp(p + new Point(0, 120), MouseButton.Left);
        Pump();
        Assert.False(_view.SheetOpen);

        LongPress(Card(b));
        // moving without pressing does nothing
        _window.MouseMove(At(handle) + new Point(0, 100));
        Assert.True(_view.SheetOpen);
        _view.HideSheet();
    }

    [AvaloniaFact]
    public void Android_back_closes_the_sheet_then_the_list_then_the_tab()
    {
        var b = MakeBoard(("A", new[] { "a" }));
        Show();
        _view.ShowBoard(b);
        Pump();
        Click(Find<Button>(_view, x => NameOf(x) == "List menu")!);
        Assert.True(_view.SheetOpen);
        Assert.True(_view.GoBack());
        Assert.False(_view.SheetOpen);
        Assert.NotNull(_view.OpenBoard);
        Assert.True(_view.GoBack());
        Assert.Null(_view.OpenBoard);
        Assert.False(_view.GoBack()); // on the lists: Android closes the app
        _view.ShowSection(PhoneSection.Settings);
        Assert.True(_view.GoBack());
        Assert.Equal(PhoneSection.Lists, _view.Section);
    }

    [AvaloniaFact]
    public void The_archive_tab_groups_by_day_and_tapping_a_row_opens_the_sheet()
    {
        var b = MakeBoard(("Work", new[] { "milk", "bread" }));
        foreach (var i in b.Categories[0].Items.ToList()) Actions.Complete(i);
        Show();
        _view.ShowSection(PhoneSection.Archive);
        Pump();
        Assert.IsType<ArchiveDayHeader>(_view.ArchiveRows[0]);
        Assert.Equal(3, _view.ArchiveRows.Count);
        var row = Find<ListBoxItem>(_view, i => i.DataContext is ArchivedItem)!;
        Tap(row);
        Assert.Equal(new[] { "Restore", "Delete permanently" }, Options());
    }

    [AvaloniaFact]
    public void Screens_move_when_motion_is_on()
    {
        PhoneView.MotionEnabled = true;
        var b = MakeBoard(("A", new[] { "a" }));
        Show();
        _view.ShowBoard(b);
        Assert.IsType<Avalonia.Media.TranslateTransform>(Named<Border>(_view, "BoardScreen").RenderTransform);
        Assert.True(UiHost.WaitUntil(() => Named<Border>(_view, "BoardScreen").RenderTransform == null)); // slid in
        Assert.True(_view.GoBack());
        Assert.True(Named<ThemeVariantScope>(_view, "BoardScope").IsVisible); // sliding out
        Assert.False(Named<Border>(_view, "BoardScreen").IsHitTestVisible);
        Assert.True(UiHost.WaitUntil(() => !Named<ThemeVariantScope>(_view, "BoardScope").IsVisible));
        Assert.True(Named<Border>(_view, "BoardScreen").IsHitTestVisible);

        // a list opened right after Back is not taken away by the old slide
        _view.ShowBoard(b);
        _view.GoBack();
        _view.ShowBoard(b);
        UiHost.Wait(TimeSpan.FromMilliseconds(500));
        Assert.True(Named<ThemeVariantScope>(_view, "BoardScope").IsVisible);

        // tabs fade, sheets slide up
        Click(Named<Button>(_view, "TabArchive"));
        Assert.True(UiHost.WaitUntil(() => Named<DockPanel>(_view, "ArchiveScreen").Opacity == 1));
        Click(Named<Button>(_view, "TabSettings"));
        Click(Named<Button>(_view, "TabLists"));
        LongPress(Card(b));
        Assert.True(_view.SheetOpen);
        Assert.True(UiHost.WaitUntil(() => Named<Border>(_view, "Sheet").RenderTransform == null));
        Assert.Equal(1, Named<Border>(_view, "Scrim").Opacity);
    }
}
