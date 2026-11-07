using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using PinList.Sync;
using Xunit;

namespace PinList.Tests;

/// <summary>
/// The "All lists" section of the PinList window: every list as a card (the phone's first screen), each with an On desktop switch. A list that
/// isn't a widget on this computer can still be opened and edited there.
/// </summary>
public class MainWindowTests : UiTest
{
    MainWindow Lists()
    {
        App.ShowListsWindow();
        Pump();
        return App.MainWindow!;
    }

    static Border Card(MainWindow w, Board b) =>
        Find<Border>(w.Lists, x => x.Classes.Contains("card") && ReferenceEquals(x.Tag, b))!;

    static ToggleSwitch WidgetSwitch(MainWindow w, Board b) => Find<ToggleSwitch>(Card(w, b))!;

    [AvaloniaFact]
    public void All_lists_with_no_lists_shows_the_empty_text_and_focuses_the_New_list_card()
    {
        var w = Lists();
        Assert.True(w.Lists.HomeEmptyVisible);
        Assert.True(Named<Button>(w.Lists, "NewListCard").IsFocused);
        Assert.Equal(PhoneView.EmptyText, A11y.LastAnnouncement);
    }

    [AvaloniaFact]
    public void All_lists_with_lists_does_not_announce_or_move_focus()
    {
        MakeBoard(("A", new string[0]));
        var w = Lists();
        Assert.False(w.Lists.HomeEmptyVisible);
        Assert.False(Named<Button>(w.Lists, "NewListCard").IsFocused);
        Assert.Null(A11y.LastAnnouncement);
    }

    [AvaloniaFact]
    public void Deleting_the_last_list_in_All_lists_puts_focus_on_the_New_list_card()
    {
        var b = MakeBoard(("A", new string[0]));
        Actions.SetWidget(b, false); // not a widget: nothing else reopens All lists
        var w = Lists();
        A11y.LastAnnouncement = null;
        Actions.DeleteBoard(b);
        Pump();
        Assert.True(Named<Button>(w.Lists, "NewListCard").IsFocused);
        Assert.Equal(PhoneView.EmptyText, A11y.LastAnnouncement);
    }

    [AvaloniaFact]
    public void A_PinList_window_that_is_not_shown_yet_announces_nothing()
    {
        A11y.LastAnnouncement = null;
        var w = new MainWindow(); // no lists, but not on screen
        Assert.Null(A11y.LastAnnouncement);
        App.ShuttingDown = true; // so it closes instead of hiding
        w.Close();
        App.ShuttingDown = false;
    }

    [AvaloniaFact]
    public void A_list_that_is_not_a_widget_has_no_window_but_is_in_all_lists()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var board = w.Board;
        Actions.SetWidget(board, false);
        Pump();
        Assert.DoesNotContain(App.OpenWindows, x => x.Board == board);
        Assert.Contains(board, Store.Data.Boards); // nothing is deleted

        var lists = Lists();
        Assert.True(lists.IsVisible);
        Assert.Equal("PinList", lists.Title);
        Assert.Equal(MainSection.Lists, lists.Section);
        Assert.NotNull(Card(lists, board));
        Assert.False(WidgetSwitch(lists, board).IsChecked);
    }

    [AvaloniaFact]
    public void The_widget_switch_opens_and_closes_the_widget_where_it_was()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var board = w.Board;
        board.X = 240;
        board.Y = 180;
        var lists = Lists();
        var toggle = WidgetSwitch(lists, board);
        Assert.True(toggle.IsChecked);

        toggle.IsChecked = false;
        Pump();
        Assert.False(board.Widget);
        Assert.DoesNotContain(App.OpenWindows, x => x.Board == board);

        toggle.IsChecked = true;
        Pump();
        Assert.True(board.Widget);
        var back = Assert.Single(App.OpenWindows, x => x.Board == board);
        Assert.Equal((240.0, 180.0), (back.Board.X, back.Board.Y));
        Assert.False(lists.Lists.OpenBoard != null); // clicking the switch doesn't open the list
    }

    [AvaloniaFact]
    public void Undo_brings_a_removed_widget_back()
    {
        var w = OpenNew(("A", new[] { "a" }));
        ChooseFromMenu(WidgetMenu(w), "Remove from desktop");
        Assert.False(w.Board.Widget);
        Assert.DoesNotContain(App.OpenWindows, x => x.Board == w.Board);
        Assert.True(Actions.Undo(out _));
        Pump();
        Assert.Single(App.OpenWindows, x => x.Board == w.Board);
    }

    [AvaloniaFact]
    public void A_list_can_be_edited_in_all_lists_while_its_widget_is_open()
    {
        var w = OpenNew(("Shopping", new[] { "Milk" }));
        var lists = Lists();
        lists.Lists.ShowBoard(w.Board);
        Pump();
        var add = Find<TextBox>(lists.Lists, t => t.Classes.Contains("phoneadd"))!;
        add.Text = "Bread";
        Press(add, Key.Enter);
        Assert.Equal(new[] { "Milk", "Bread" }, w.Board.Categories[0].Items.Select(i => i.Text));
        Assert.NotNull(Row(w, w.Board.Categories[0].Items[1])); // the widget shows it too
    }

    [AvaloniaFact]
    public void Right_click_on_a_card_opens_its_menu()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var lists = Lists();
        var card = Card(lists, w.Board);
        var p = card.TranslatePoint(new Point(card.Bounds.Width / 2, 12), lists)!.Value;
        lists.MouseDown(p, MouseButton.Right);
        lists.MouseUp(p, MouseButton.Right);
        Pump();
        Assert.True(lists.Lists.SheetOpen);
        Assert.Null(lists.Lists.OpenBoard); // a menu, not the list
    }

    [AvaloniaFact]
    public void All_lists_has_no_bottom_bar_and_no_phone_button()
    {
        var lists = Lists();
        Assert.False(lists.Lists.BottomBarVisible);
        Assert.False(lists.Lists.AddBoardButtonVisible);
    }

    [AvaloniaFact]
    public void No_widgets_opens_all_lists_instead_and_adds_no_list()
    {
        var w = OpenNew(("A", new[] { "a" }));
        foreach (var b in Store.Data.Boards) b.Widget = false;
        foreach (var x in App.OpenWindows.ToList()) x.CloseForReload();
        int count = Store.Data.Boards.Count;
        App.OpenAllBoards();
        Pump();
        Assert.Empty(App.OpenWindows);
        Assert.True(App.MainWindow?.IsVisible);
        Assert.Equal(count, Store.Data.Boards.Count);

        App.MainWindow!.Hide();
        App.ShowAllBoards(); // the tray icon or starting PinList again
        Pump();
        Assert.True(App.MainWindow.IsVisible);
    }

    [AvaloniaFact]
    public void A_list_deleted_elsewhere_leaves_all_lists()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var board = w.Board;
        Actions.SetWidget(board, false);
        var lists = Lists();
        Assert.NotNull(Card(lists, board));
        Store.Data.Boards.Remove(board); // what sync does when the list was deleted on another device
        Pump();
        Assert.Null(Find<Border>(lists.Lists, x => x.Classes.Contains("card") && ReferenceEquals(x.Tag, board)));
    }

    [AvaloniaFact]
    public void Closing_all_lists_only_hides_it()
    {
        var lists = Lists();
        lists.Close();
        Pump();
        Assert.False(lists.IsVisible);
        Assert.Same(lists, App.MainWindow);
        App.ShowListsWindow();
        Pump();
        Assert.True(lists.IsVisible);
    }

    [AvaloniaFact]
    public void All_lists_is_in_the_tray_and_widget_menus()
    {
        Assert.Equal("All lists", App.TrayMenuEntries().First(e => e != TrayEntry.Separator && !e.Text.StartsWith("Get ")).Text);
        ChooseFromMenu(App.ShowTrayMenu(), "All lists");
        Assert.True(App.MainWindow?.IsVisible);
        App.MainWindow!.Hide();

        var w = OpenNew(("A", new[] { "a" }));
        ChooseFromMenu(WidgetMenu(w), "All lists");
        Assert.True(App.MainWindow.IsVisible);
    }

    [AvaloniaFact]
    public void Deleting_a_synced_widget_offers_to_remove_it_from_the_desktop_instead()
    {
        var w = OpenNew(("A", new[] { "a" }));
        w.Board.Title = "Groceries";
        w.Board.Synced = true;
        Dialogs.ChooseAnswer = _ => 1; // the middle button
        ChooseFromMenu(WidgetMenu(w), "Delete list");
        Assert.True(UiHost.WaitUntil(() => !w.Board.Widget));
        Assert.Contains(w.Board, Store.Data.Boards); // not deleted
        Assert.DoesNotContain(App.OpenWindows, x => x.Board == w.Board);
    }

    [AvaloniaFact]
    public void Deleting_a_synced_widget_still_deletes_it_with_Delete_and_keeps_it_with_Cancel()
    {
        var w = OpenNew(("A", new[] { "a" }));
        w.Board.Synced = true;
        ChooseFromMenu(WidgetMenu(w), "Delete list"); // Esc
        Pump();
        Assert.Contains(w.Board, Store.Data.Boards);
        Assert.True(w.Board.Widget);

        Dialogs.ChooseAnswer = _ => 2;
        ChooseFromMenu(WidgetMenu(w), "Delete list");
        Assert.True(UiHost.WaitUntil(() => !Store.Data.Boards.Contains(w.Board)));
    }

    [AvaloniaFact]
    public void A_minimized_all_lists_comes_back()
    {
        var lists = Lists();
        lists.WindowState = WindowState.Minimized;
        App.ShowListsWindow();
        Assert.Equal(WindowState.Normal, lists.WindowState);
    }

    [AvaloniaFact]
    public void Opening_the_widgets_again_refreshes_all_lists()
    {
        var lists = Lists();
        var added = MakeBoard(("New", new[] { "n" })); // e.g. an import replaced the lists
        App.OpenAllBoards();
        Pump();
        Assert.NotNull(Card(lists, added));
    }

    [AvaloniaFact]
    public void Widget_changes_for_a_list_without_a_window_or_that_is_gone_open_nothing()
    {
        var board = MakeBoard(("A", new[] { "a" })); // a list with no widget window
        Actions.SetWidget(board, false);
        Pump();
        Assert.False(board.Widget);

        Store.Data.Boards.Remove(board);
        Actions.SetWidget(board, true); // gone meanwhile (deleted on another device)
        Pump();
        Assert.DoesNotContain(App.OpenWindows, x => x.Board == board);
    }

    [AvaloniaFact]
    public void Clicking_the_widget_switch_does_not_open_the_list()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var lists = Lists();
        var toggle = WidgetSwitch(lists, w.Board);
        var p = toggle.TranslatePoint(new Point(10, toggle.Bounds.Height / 2), lists)!.Value;
        lists.MouseDown(p, MouseButton.Left);
        lists.MouseUp(p, MouseButton.Left);
        Pump();
        Assert.False(w.Board.Widget);
        Assert.Null(lists.Lists.OpenBoard);

        var card = Card(lists, w.Board); // clicking the card itself opens the list
        var q = card.TranslatePoint(new Point(20, card.Bounds.Height - 12), lists)!.Value;
        lists.MouseDown(q, MouseButton.Left);
        lists.MouseUp(q, MouseButton.Left);
        Pump();
        Assert.Same(w.Board, lists.Lists.OpenBoard);
    }

    static void ChooseInSheet(MainWindow lists, string option)
    {
        Pump();
        Click(lists.Lists.SheetPanel!.Children.OfType<Button>().First(b => b.Content as string == option));
        Pump();
    }

    /// <summary>Review finding: the widget stayed open on a list that no longer existed.</summary>
    [AvaloniaFact]
    public void Deleting_a_list_in_all_lists_closes_its_widget()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var lists = Lists();
        lists.Lists.ShowBoardMenu(w.Board, fromCard: true);
        ChooseInSheet(lists, "Delete list");
        ChooseInSheet(lists, "Delete");
        Assert.DoesNotContain(w.Board, Store.Data.Boards);
        Assert.DoesNotContain(App.OpenWindows, x => x.Board == w.Board);
        Assert.False(w.IsVisible);
    }

    /// <summary>Review finding: the hidden window could say no to a Windows shutdown.</summary>
    [AvaloniaFact]
    public void Windows_shutdown_closes_all_lists()
    {
        var lists = Lists();
        try
        {
            typeof(Window).GetMethod("CloseCore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(lists, new object[] { WindowCloseReason.OSShutdown, false, false });
            Pump();
            Assert.False(lists.IsVisible);
            Assert.True(App.ShuttingDown);
            Assert.Null(App.MainWindow); // closed for good, so it isn't shown again as a dead window
        }
        finally { App.ShuttingDown = false; }
    }

    /// <summary>Review finding: removing the last widget left nothing on screen to get the lists back.</summary>
    [AvaloniaFact]
    public void Removing_the_last_widget_opens_all_lists()
    {
        var w = OpenNew(("A", new[] { "a" }));
        Assert.Single(App.OpenWindows);
        ChooseFromMenu(WidgetMenu(w), "Remove from desktop");
        Assert.Empty(App.OpenWindows);
        Assert.True(App.MainWindow?.IsVisible);
    }

    /// <summary>Press the mouse on a control and move it a bit with the button down (a mouse drag starts).</summary>
    static Point Press(MainWindow lists, Control c, double moveBy = 10)
    {
        var p = c.TranslatePoint(new Point(10, c.Bounds.Height / 2), lists)!.Value;
        lists.MouseDown(p, MouseButton.Left);
        p += new Point(0, moveBy);
        lists.MouseMove(p, RawInputModifiers.LeftMouseButton);
        Pump();
        return p;
    }

    [AvaloniaFact]
    public void Sync_waits_while_a_card_is_dragged_in_all_lists()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var lists = Lists();
        Assert.False(SyncService.Current!.IsBusy(w.Board));
        var p = Press(lists, Card(lists, w.Board));
        Assert.NotNull(lists.Lists.HomeDrag.Lifted);
        Assert.True(SyncService.Current.IsBusy(w.Board));
        lists.MouseUp(p, MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(500));
        Assert.False(SyncService.Current.IsBusy(w.Board));
    }

    [AvaloniaFact]
    public void Holding_the_widget_switch_does_not_lift_the_card()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var lists = Lists();
        var p = Press(lists, WidgetSwitch(lists, w.Board));
        Assert.Null(lists.Lists.HomeDrag.Lifted);
        lists.MouseUp(p, MouseButton.Left);
        Pump();
    }

    [AvaloniaFact]
    public void Cards_are_dragged_with_the_mouse_in_all_lists_as_one_undoable_move()
    {
        var a = OpenNew(("A", new[] { "a" }));
        var b = OpenNew(("B", new[] { "b" }));
        var lists = Lists();
        Assert.Equal(new[] { a.Board, b.Board }, Store.Data.Boards);

        // holding the mouse still is not a long-press here (a right click opens the menu)
        var still = Card(lists, b.Board).TranslatePoint(new Point(10, 10), lists)!.Value;
        lists.MouseDown(still, MouseButton.Left);
        lists.MouseMove(still + new Point(2, 2), RawInputModifiers.LeftMouseButton); // a tiny move is still a click
        UiHost.Wait(PhoneDrag.HoldTime + TimeSpan.FromMilliseconds(150));
        Assert.Null(lists.Lists.HomeDrag.Lifted);
        Assert.False(lists.Lists.SheetOpen);
        lists.MouseUp(still, MouseButton.Left);
        Pump();
        lists.Lists.ShowHome();
        Pump();

        // drag B above A
        var p = Press(lists, Card(lists, b.Board));
        Assert.Same(b.Board, lists.Lists.HomeDrag.Lifted);
        var target = Card(lists, a.Board).TranslatePoint(new Point(10, 5), lists)!.Value;
        lists.MouseMove(target, RawInputModifiers.LeftMouseButton);
        Pump();
        lists.MouseUp(target, MouseButton.Left);
        Pump();
        Assert.Equal(new[] { b.Board, a.Board }, Store.Data.Boards);
        Assert.Null(lists.Lists.OpenBoard); // the drop isn't also a click that opens the list

        // the moved list gets a position before the other one, so sync sends the new order
        Store.SaveNow();
        Assert.True(b.Board.Position < a.Board.Position);

        Actions.Undo();
        Assert.Equal(new[] { a.Board, b.Board }, Store.Data.Boards);
    }

    [AvaloniaFact]
    public void Cards_and_the_open_list_follow_the_widget_color()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var lists = Lists();
        var before = ((Avalonia.Media.ISolidColorBrush)Card(lists, w.Board).Background!).Color;
        lists.Lists.ShowBoard(w.Board);
        Pump();
        ChooseSwatch(WidgetMenu(w), "List color", "Light");
        Pump();
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, Find<ThemeVariantScope>(lists.Lists)!.RequestedThemeVariant);
        lists.Lists.GoBack();
        Pump();
        Assert.NotEqual(before, ((Avalonia.Media.ISolidColorBrush)Card(lists, w.Board).Background!).Color);
    }

    [AvaloniaFact]
    public void A_copy_that_fails_says_so()
    {
        var w = OpenNew(("Shopping", new[] { "Milk" }));
        var lists = Lists();
        Copying.SetClipboard = (_, _) => throw new System.Runtime.InteropServices.ExternalException("busy");
        try
        {
            lists.Lists.ShowBoardMenu(w.Board, fromCard: true);
            ChooseInSheet(lists, "Copy all items");
            Assert.True(UiHost.WaitUntil(() => lists.Lists.SnackbarMessage == Copying.Failed));
            ChooseFromMenu(WidgetMenu(w), "Copy all items");
            Assert.True(UiHost.WaitUntil(() => A11y.LastAnnouncement == Copying.Failed));
        }
        finally { Copying.SetClipboard = Copying.SystemClipboard; }
    }

    static ContextMenu WidgetMenu(BoardWindow w)
    {
        Click(Named<Button>(w, "MenuBtn"));
        return w.LastMenu!;
    }

    // ================= The sections =================

    static Button Nav(MainWindow w, string name) => Named<Button>(w, name);

    [AvaloniaFact]
    public void Each_entry_point_opens_the_right_section()
    {
        App.ShowListsWindow();
        Pump();
        var window = App.MainWindow!;
        Assert.Equal(MainSection.Lists, window.Section);
        App.ShowArchive();
        Assert.Same(window, App.MainWindow); // one window for all of them
        Assert.Equal(MainSection.Archive, window.Section);
        Assert.True(window.Archive.IsVisible);
        Assert.False(window.Lists.IsVisible);
        App.ShowSettings();
        Assert.Equal(MainSection.Settings, window.Section);
        Assert.True(window.Settings.IsVisible);
        Assert.False(window.Archive.IsVisible);
        App.ShowListsWindow();
        Assert.Equal(MainSection.Lists, window.Section);
        Assert.True(window.Lists.IsVisible);
    }

    [AvaloniaFact]
    public void Sidebar_switches_sections()
    {
        var w = Lists();
        Assert.Contains("selected", Nav(w, "NavLists").Classes);
        Click(Nav(w, "NavArchive"));
        Assert.Equal(MainSection.Archive, w.Section);
        Assert.Contains("selected", Nav(w, "NavArchive").Classes);
        Assert.DoesNotContain("selected", Nav(w, "NavLists").Classes);
        Click(Nav(w, "NavSettings"));
        Assert.Equal(MainSection.Settings, w.Section);
        Assert.Contains("selected", Nav(w, "NavSettings").Classes);
        Click(Nav(w, "NavLists"));
        Assert.Equal(MainSection.Lists, w.Section);
        Assert.Equal("All lists", NameOf(Nav(w, "NavLists")));
        Assert.Equal("Archive", NameOf(Nav(w, "NavArchive")));
        Assert.Equal("Settings", NameOf(Nav(w, "NavSettings")));
    }

    [AvaloniaFact]
    public void The_sidebar_counts_lists_and_archived_items()
    {
        var board = MakeBoard(("Work", new[] { "a", "b" }));
        var w = Lists();
        Assert.Equal("1", Named<TextBlock>(w, "ListsCount").Text);
        Assert.Equal("0", Named<TextBlock>(w, "ArchiveCount").Text);
        Actions.Complete(board.Categories[0].Items[0]);
        Assert.Equal("1", Named<TextBlock>(w, "ArchiveCount").Text);
        MakeBoard();
        Store.Data.Boards.Add(new Board());
        Assert.Equal("3", Named<TextBlock>(w, "ListsCount").Text);
    }

    [AvaloniaFact]
    public void Opening_a_section_again_keeps_the_window_where_it_was()
    {
        var first = Lists();
        first.Width = 777;
        App.ShowArchive();
        Assert.Same(first, App.MainWindow);
        Assert.Equal(777, first.Width);
        first.WindowState = WindowState.Maximized;
        App.ShowSettings();
        Assert.Equal(WindowState.Maximized, first.WindowState); // only a minimized one is restored
        first.WindowState = WindowState.Minimized;
        App.ShowArchive();
        Assert.Equal(WindowState.Normal, first.WindowState);
    }

    [AvaloniaFact]
    public void Closing_hides_and_shutdown_closes()
    {
        var w = Lists();
        w.Close();
        Pump();
        Assert.False(w.IsVisible);
        Assert.Same(w, App.MainWindow);
        App.ShowSettings();
        Assert.True(w.IsVisible);
        Assert.Equal(MainSection.Settings, w.Section);
        App.CloseMainWindow();
        Assert.Null(App.MainWindow);
    }

    [AvaloniaFact]
    public void Settings_section_has_the_settings_page_and_follows_changes()
    {
        App.ShowSettings();
        Pump();
        var window = App.MainWindow!;
        var texts = FindAll<TextBlock>(window.Settings).Select(t => t.Text).ToList();
        Assert.Contains("Settings", texts);
        Assert.Contains("Appearance", texts);
        Assert.Contains("Your data", texts);
        Click(FindAll<ToggleButton>(window.Settings).First(b => b.Classes.Contains("seg") && b.Content as string == "Light"));
        Assert.Equal("Light", Store.Data.Settings.Theme);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, App.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void The_window_follows_the_theme_and_text_size()
    {
        var w = Lists();
        int version = ChromeLook.GetVersion(w);
        Assert.Equal("Dark", ChromeLook.GetStyleKey(w));
        App.SetTheme("Light");
        Assert.Equal("Light", ChromeLook.GetStyleKey(w));
        Assert.True(ChromeLook.GetVersion(w) > version);
        A11y.TextScaleSource = () => 1.5;
        App.RefreshAllWindows();
        Assert.Equal(1.5, ((ScaleTransform)Named<LayoutTransformControl>(w, "Scaler").LayoutTransform!).ScaleX);
        A11y.TextScaleSource = () => 1.0;
        App.RefreshAllWindows();
        Assert.Null(Named<LayoutTransformControl>(w, "Scaler").LayoutTransform);
        App.CloseMainWindow();
        App.SetTheme("Dark"); // a closed window no longer listens
    }

    [AvaloniaFact]
    public void The_new_list_card_starts_a_list()
    {
        var w = Lists();
        int boards = Store.Data.Boards.Count;
        var card = Named<Button>(w.Lists, "NewListCard");
        Assert.True(card.IsVisible);
        Assert.Equal("New list", NameOf(card));
        Assert.False(w.Lists.AddBoardButtonVisible); // the desktop has the card, not the phone's button
        Click(card);
        Assert.Equal(boards + 1, Store.Data.Boards.Count);
        Assert.NotNull(w.Lists.OpenBoard);
        Assert.False(w.Lists.AddBoardButtonVisible);
        w.Lists.GoBack();
        Assert.False(w.Lists.AddBoardButtonVisible);
    }

    [AvaloniaFact]
    public void An_import_that_replaces_everything_refreshes_the_window()
    {
        var w = Lists();
        MakeBoard(("Old", new[] { "a" }));
        w.Reload();
        Assert.Equal("1", Named<TextBlock>(w, "ListsCount").Text);
        Store.ReplaceAll(new AppData());
        w.Reload();
        Assert.Equal("0", Named<TextBlock>(w, "ListsCount").Text);
        Assert.Empty(w.Archive.Shown);
    }
}
