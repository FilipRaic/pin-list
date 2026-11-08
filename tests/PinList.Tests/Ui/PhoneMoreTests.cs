using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace PinList.Tests;

/// <summary>Phone screens in situations the other phone tests don't reach.</summary>
public partial class PhoneTests
{
    [AvaloniaFact]
    public void The_phone_calls_the_cards_screen_Lists()
    {
        var view = new PhoneView(desktop: false);
        Assert.False(view.IsDesktop);
        Assert.Equal("Lists", Named<TextBlock>(view, "HomeTitle").Text);
        Assert.False(Named<Control>(view, "NewListCard").IsVisible);
    }

    [AvaloniaFact]
    public void Cards_of_light_and_dark_lists_use_their_own_accent()
    {
        static Avalonia.Media.Color AccentOf(string style)
        {
            using var card = new BoardCard(new Board { Style = style });
            return Color(card.Accent);
        }

        Assert.Equal(Avalonia.Media.Color.Parse("#60CDFF"), AccentOf("Dark"));
        Assert.Equal(Avalonia.Media.Color.Parse("#005FB8"), AccentOf("Light"));
    }

    [AvaloniaFact]
    public void The_first_list_gets_the_light_look_and_its_title_is_edited()
    {
        Show();
        Click(Named<Button>(_view, "AddBoardButton"));
        Pump();

        var board = Assert.Single(Store.Data.Boards);
        Assert.Same(board, _view.OpenBoard);
        Assert.Equal("", board.Title);
        Assert.Equal("Light", board.Style);
        Assert.True(board.Transparent);
    }

    [AvaloniaFact]
    public void Deleting_a_list_from_its_card_stays_on_the_cards()
    {
        var keep = MakeBoard(("A", new[] { "a" }));
        var gone = MakeBoard(("B", new string[0]));
        Show();

        _view.ShowBoardMenu(gone, fromCard: true);
        Pump();
        Choose("Delete list");
        Assert.Equal("Delete \"Test\"?", _view.SheetTitleText); // no items, nothing more to say
        Choose("Delete");
        Pump();

        Assert.Equal(new[] { keep }, Store.Data.Boards);
        Assert.Null(_view.OpenBoard);
        Assert.Single(_view.CardList);
    }

    [AvaloniaFact]
    public void Deleting_the_open_list_goes_back_to_the_cards()
    {
        var board = MakeBoard(("A", new[] { "a", "b" }));
        Show();
        _view.ShowBoard(board);
        Pump();

        _view.ShowBoardMenu(board, fromCard: false);
        Pump();
        Choose("Delete list");
        Assert.Equal("Delete \"Test\" with 2 items? They don't go to the archive.", _view.SheetTitleText);
        Choose("Delete");
        Pump();

        Assert.Empty(Store.Data.Boards);
        Assert.Null(_view.OpenBoard);
        Assert.True(_view.HomeEmptyVisible);
    }

    [AvaloniaFact]
    public void New_list_gets_the_keyboard_also_when_there_are_lists()
    {
        MakeBoard(("A", new[] { "a" }));
        Show();
        A11y.Announce(_view, "before");

        _view.FocusNewList();
        Pump();

        Assert.True(Named<Button>(_view, "AddBoardButton").IsFocused);
        Assert.Equal("before", A11y.LastAnnouncement); // the empty text is only for no lists
    }

    [AvaloniaFact]
    public void Loading_other_data_while_a_list_is_open_goes_back_to_the_cards()
    {
        var board = MakeBoard(("A", new[] { "a" }));
        Show();
        _view.ShowBoard(board);
        Pump();

        Store.UseDirectory(Dir); // other data, without that list
        MakeBoard(("B", new[] { "b" }));
        _view.Reload();
        Pump();

        Assert.Null(_view.OpenBoard);
        Assert.Single(_view.CardList);
    }

    [AvaloniaFact]
    public void A_theme_change_recolors_the_open_list_and_the_settings_page()
    {
        var board = MakeBoard(("A", new[] { "a" }));
        Show();
        _view.App_ThemeOrDataChanged(); // nothing open yet, no settings page yet

        _view.ShowSection(PhoneSection.Settings);
        Pump();
        _view.ShowSection(PhoneSection.Lists);
        _view.ShowBoard(board);
        Pump();
        _view.App_ThemeOrDataChanged();
        Pump();

        Assert.Same(board, _view.OpenBoard);
        Assert.NotNull(_view.SettingsPage);
    }

    /// <summary>Drags one row onto another and lets go, then taps right away (within the "no tap after a drag" time).</summary>
    void DragThenTap(Control from, Control to, Control tapped)
    {
        var p = At(from);
        _window.MouseDown(p, MouseButton.Left);
        UiHost.Wait(TimeSpan.FromMilliseconds(150));
        var q = At(to, 0.75);
        _window.MouseMove(new Avalonia.Point(q.X, (p.Y + q.Y) / 2), RawInputModifiers.LeftMouseButton);
        Pump();
        _window.MouseMove(q, RawInputModifiers.LeftMouseButton);
        Pump();
        _window.MouseUp(q, MouseButton.Left);
        Pump();
        Tap(tapped);
    }

    [AvaloniaFact]
    public void A_tap_right_after_dragging_a_card_does_not_open_a_list()
    {
        var a = MakeBoard(("A", new[] { "a" }));
        var b = MakeBoard(("B", new[] { "b" }));
        Show();

        DragThenTap(Card(a), Card(b), Card(b));

        Assert.Equal(new[] { b, a }, Store.Data.Boards); // the drag moved it
        Assert.Null(_view.OpenBoard); // the tap was part of the drag
    }

    [AvaloniaFact]
    public void A_tap_right_after_dragging_an_item_neither_edits_nor_collapses()
    {
        var board = MakeBoard(("A", new[] { "x", "y", "z" }));
        var cat = board.Categories[0];
        var (x, z) = (cat.Items[0], cat.Items[2]);
        Show();
        _view.ShowBoard(board);
        Pump();

        DragThenTap(PhoneRow(x), PhoneRow(z), PhoneRow(z));
        Assert.Equal(new[] { "y", "z", "x" }, cat.Items.Select(i => i.Text));
        Assert.False(z.IsEditing);

        DragThenTap(PhoneRow(x), PhoneRow(z), PhoneRow(cat));
        Assert.False(cat.Collapsed);
    }

    [AvaloniaFact]
    public void A_list_from_a_newer_PinList_explains_its_sync_problem()
    {
        var board = MakeBoard(("A", new[] { "a" }));
        ShowWithSync();
        PhoneSignIn();
        LongPress(Card(board));
        Choose("Sync this list");
        _view.ShowBoard(board);
        _phoneCloud.Put($"w-{board.Id}.other.json", $$"""{ "format": 99, "boardId": "{{board.Id}}" }""");
        Await(_view.Sync!.SyncNow());
        Pump();

        var problem = _view.Sync.StateOf(board).Problem;
        Assert.NotNull(problem);
        Assert.NotEqual("Can't reach Google Drive.", problem);
        Click(HeaderSyncBtn());
        Assert.Equal(problem, _view.SheetTitleText); // shown as it is
        Choose("OK");
        Assert.False(_view.SheetOpen);
    }
}
