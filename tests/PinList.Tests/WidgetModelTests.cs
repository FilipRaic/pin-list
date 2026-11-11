using Avalonia.Controls;
using Avalonia.Media;
using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>What an Android home screen widget shows for a list (WidgetModel).</summary>
public class WidgetModelTests
{
    static Board MakeBoard(AppData data, string title, params (string name, string[] items)[] categories)
    {
        var b = new Board { Title = title, Style = "Blue", Transparent = false };
        foreach (var (name, items) in categories)
        {
            var c = new Category { Name = name };
            foreach (var t in items) c.Items.Add(new TodoItem { Text = t });
            b.Categories.Add(c);
        }
        data.Boards.Add(b);
        return b;
    }

    static uint BoardColor(Board board, string key)
    {
        var r = new ResourceDictionary();
        Themes.ApplyBoard(r, Themes.Get(board.Style), board.Transparent);
        return ((ISolidColorBrush)r[key]!).Color.ToUInt32();
    }

    [Fact]
    public void Rows_follow_the_list_and_skip_items_of_collapsed_categories()
    {
        var data = new AppData();
        var board = MakeBoard(data, "Work", ("Now", new[] { "a", "b" }), ("Later", new[] { "c" }));
        board.Categories[1].Collapsed = true;

        var state = WidgetModel.For(data, board.Id, false);

        Assert.Equal(WidgetStatus.Ok, state.Status);
        Assert.Equal("Work", state.Title);
        Assert.Equal(board.Id, state.BoardId);
        Assert.Equal(new[] { WidgetRowKind.Category, WidgetRowKind.Item, WidgetRowKind.Item, WidgetRowKind.Category },
            state.Rows.Select(r => r.Kind));
        Assert.Equal(("Now", 2, false, board.Categories[0].Id), (state.Rows[0].Text, state.Rows[0].Count, state.Rows[0].Collapsed, state.Rows[0].Id));
        Assert.Equal(("a", board.Categories[0].Items[0].Id), (state.Rows[1].Text, state.Rows[1].Id));
        Assert.Equal(("Later", 1, true), (state.Rows[3].Text, state.Rows[3].Count, state.Rows[3].Collapsed));
        Assert.Equal("Now, 2 items", state.Rows[0].AccessibleName);
        Assert.Equal("a", state.Rows[1].AccessibleName);
    }

    [Fact]
    public void An_overdue_item_has_the_overdue_text_and_color()
    {
        var data = new AppData();
        var board = MakeBoard(data, "Work", ("Now", new[] { "late", "today", "later", "plain" }));
        var items = board.Categories[0].Items;
        items[0].DueDate = DateTime.Today.AddDays(-3);
        items[1].DueDate = DateTime.Today;
        items[2].DueDate = DateTime.Today.AddDays(1);
        items[2].ReminderAt = DateTime.Now.AddDays(1);

        var rows = WidgetModel.For(data, board.Id, false).Rows;

        Assert.Equal(("3 days overdue", BoardColor(board, "Overdue"), BoardColor(board, "OverdueBg")), (rows[1].Due, rows[1].DueColor, rows[1].DueBackground));
        Assert.Equal(("Today", BoardColor(board, "Accent"), BoardColor(board, "AccentBg")), (rows[2].Due, rows[2].DueColor, rows[2].DueBackground));
        Assert.Equal(("Tomorrow", BoardColor(board, "TextSecondary"), BoardColor(board, "ChipBg")), (rows[3].Due, rows[3].DueColor, rows[3].DueBackground));
        Assert.Equal("", rows[4].Due);
        Assert.True(rows[3].HasReminder);
        Assert.False(rows[4].HasReminder);
    }

    [Fact]
    public void A_list_that_is_gone_gives_the_deleted_state()
    {
        var data = new AppData();
        MakeBoard(data, "Work", ("Now", new[] { "a" })).Synced = true;

        foreach (var id in new[] { "nope", null })
        {
            var state = WidgetModel.For(data, id, true);
            Assert.Equal(WidgetStatus.Deleted, state.Status);
            Assert.Empty(state.Rows);
            Assert.False(state.CanSync);
            Assert.False(state.HasCategories);
            Assert.Equal("", state.Title);
            Assert.True(state.Look.IsDark);
        }
    }

    [Fact]
    public void A_list_without_items_is_empty_but_keeps_its_category_rows()
    {
        var data = new AppData();
        var board = MakeBoard(data, "Work", ("Now", new string[0]));

        var state = WidgetModel.For(data, board.Id, false);

        Assert.Equal(WidgetStatus.Empty, state.Status);
        Assert.Equal(WidgetRowKind.Category, Assert.Single(state.Rows).Kind);
        Assert.True(state.HasCategories);
    }

    [Fact]
    public void A_list_without_categories_says_so()
    {
        var data = new AppData();
        var board = MakeBoard(data, "Work");

        var state = WidgetModel.For(data, board.Id, false);

        Assert.Equal(WidgetStatus.Empty, state.Status);
        Assert.Empty(state.Rows);
        Assert.False(state.HasCategories);
    }

    [Fact]
    public void Windows_line_breaks_become_plain_ones_and_long_text_is_kept()
    {
        var data = new AppData();
        string longText = new string('x', 500);
        var board = MakeBoard(data, "Work", ("Now", new[] { "a\r\nb", longText }));

        var rows = WidgetModel.For(data, board.Id, false).Rows;

        Assert.Equal("a\nb", rows[1].Text);
        Assert.Equal(longText, rows[2].Text);
    }

    [Fact]
    public void The_background_is_90_percent_when_transparent_and_solid_when_not()
    {
        var data = new AppData();
        var board = MakeBoard(data, "Work", ("Now", new[] { "a" }));

        board.Transparent = true;
        Assert.Equal(0xE6D6EAFAu, WidgetModel.For(data, board.Id, false).Look.Background);
        board.Transparent = false;
        var look = WidgetModel.For(data, board.Id, false).Look;
        Assert.Equal(0xFFD6EAFAu, look.Background);
        Assert.False(look.IsDark);
        Assert.Equal(BoardColor(board, "TextPrimary"), look.Text);
        Assert.Equal(BoardColor(board, "TextMuted"), look.Muted);
        Assert.Equal(BoardColor(board, "Accent"), look.Accent);
        Assert.Equal(BoardColor(board, "Line"), look.Line);
    }

    [Fact]
    public void Category_colors_are_the_shades_the_app_shows()
    {
        var data = new AppData();
        var board = MakeBoard(data, "Work", ("Now", new[] { "a" }));
        var cat = board.Categories[0];
        cat.Color = "#FFB900";

        var rows = WidgetModel.For(data, board.Id, false).Rows;

        Assert.Equal(Themes.CategoryColor(cat.Color, board.Style, CategoryPart.Text).ToUInt32(), rows[0].Color);
        Assert.Equal(Themes.CategoryColor(cat.Color, board.Style, CategoryPart.Pill).ToUInt32(), rows[0].PillColor);
        Assert.Equal(Themes.CategoryColor(cat.Color, board.Style, CategoryPart.Ring).ToUInt32(), rows[1].Color);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void The_refresh_button_needs_a_synced_list_and_a_sign_in(bool signedIn, bool synced, bool expected)
    {
        var data = new AppData();
        var board = MakeBoard(data, "Work", ("Now", new[] { "a" }));
        board.Synced = synced;

        Assert.Equal(expected, WidgetModel.For(data, board.Id, signedIn).CanSync);
    }

    [Fact]
    public void Choices_list_every_list_with_its_open_items()
    {
        var data = new AppData();
        var work = MakeBoard(data, "Work", ("Now", new[] { "a", "b" }), ("Later", new[] { "c" }));
        var untitled = MakeBoard(data, " ", ("Now", new string[0]));
        untitled.Style = "Dark";
        untitled.Transparent = true;

        var choices = WidgetModel.Choices(data);

        Assert.Equal(new[] { work.Id, untitled.Id }, choices.Select(c => c.BoardId));
        Assert.Equal(new[] { "Work", "No title" }, choices.Select(c => c.Title));
        Assert.Equal(new[] { 3, 0 }, choices.Select(c => c.OpenCount));
        Assert.Equal(new[] { 0xFFD6EAFAu, 0xFF202020u }, choices.Select(c => c.Swatch));
        Assert.Equal(new[] { "3 items", "0 items" }, choices.Select(c => c.CountText));
        Assert.Equal("1 item", new WidgetChoice("x", "X", 0, 1).CountText);
    }

    [Fact]
    public void The_add_title_names_the_list_or_says_add_item()
    {
        Assert.Equal("Add to Work", WidgetModel.AddTitle("Work"));
        Assert.Equal("Add item", WidgetModel.AddTitle(""));
        Assert.Equal("Add item", WidgetModel.AddTitle("  "));
    }

    [Fact]
    public void The_circle_is_named_after_the_first_line_of_the_item()
    {
        Assert.Equal("Complete Send the invoice", WidgetModel.CompleteName("Send the invoice"));
        Assert.Equal("Complete first", WidgetModel.CompleteName("first\r\nsecond"));
    }

    static uint ChromeColor(bool dark, string key)
    {
        var r = new ResourceDictionary();
        Themes.ApplyChrome(r, dark);
        return ((ISolidColorBrush)r[key]!).Color.ToUInt32();
    }

    /// <summary>The add box and the list chooser look like the app's own sheets: its colors, in its theme.</summary>
    [Theory]
    [InlineData("Dark", false, true)]
    [InlineData("Light", true, false)]
    [InlineData("System", true, true)]
    [InlineData("System", false, false)]
    [InlineData(null, true, true)]
    public void The_add_box_has_the_apps_colors_in_the_apps_theme(string? setting, bool systemDark, bool dark)
    {
        var chrome = WidgetModel.Chrome(setting, systemDark);

        Assert.Equal(dark, chrome.IsDark);
        Assert.Equal(ChromeColor(dark, "Raised"), chrome.Background);
        Assert.Equal(ChromeColor(dark, "Hover"), chrome.Field);
        Assert.Equal(ChromeColor(dark, "Text1"), chrome.Text);
        Assert.Equal(ChromeColor(dark, "Text3"), chrome.Muted);
        Assert.Equal(ChromeColor(dark, "Accent"), chrome.Accent);
        Assert.Equal(ChromeColor(dark, "AccentText"), chrome.AccentText);
    }

    [Fact]
    public void The_texts_of_the_sync_button()
    {
        Assert.Equal("Syncing", WidgetModel.SyncingText);
        Assert.Equal("Synced", WidgetModel.SyncedText);
        Assert.Equal("Offline. Changes sync later.", WidgetModel.OfflineText);
        Assert.Equal("Can't reach Google Drive. PinList keeps trying.", WidgetModel.SyncWaitingText);
        Assert.Equal("Sync problem. Open PinList to fix it.", WidgetModel.SyncProblemText);
    }

    [Fact]
    public void The_texts_of_the_states()
    {
        Assert.Equal("This list was deleted", WidgetModel.DeletedText);
        Assert.Equal("Choose a list", WidgetModel.ChooseText);
        Assert.Equal("No items yet", WidgetModel.EmptyText);
        Assert.Equal("No lists yet. Open PinList to create one.", WidgetModel.NoListsText);
        Assert.Equal("Completed", WidgetModel.CompletedText);
        Assert.Equal("Undo", WidgetModel.UndoText);
    }
}
