using Xunit;

namespace PinList.Tests;

/// <summary>Copying items as text (to paste them into a message, a note or another app).</summary>
public class CopyTextTests
{
    static Category Cat(string name, params string[] items)
    {
        var c = new Category { Name = name };
        foreach (var i in items) c.Items.Add(new TodoItem { Text = i });
        return c;
    }

    static Board Board(params Category[] cats)
    {
        var b = new Board();
        foreach (var c in cats) b.Categories.Add(c);
        return b;
    }

    [Fact]
    public void Category_copies_one_line_per_item() =>
        Assert.Equal("- Milk\n- Bread", CopyText.Category(Cat("Shopping", "Milk", "Bread")));

    [Fact]
    public void Multi_line_items_stay_one_item() =>
        Assert.Equal("- Send the invoice\n  to Ana", CopyText.Category(Cat("Work", "Send the invoice\r\nto Ana")));

    [Fact]
    public void Board_copies_categories_with_headings() =>
        Assert.Equal("Shopping\n- Milk\n\nWork\n- Call",
            CopyText.Board(Board(Cat("Shopping", "Milk"), Cat("Work", "Call"))));

    [Fact]
    public void Unnamed_categories_have_no_heading_and_empty_ones_are_left_out() =>
        Assert.Equal("- Milk\n\nWork\n- Call",
            CopyText.Board(Board(Cat("", "Milk"), Cat("Empty"), Cat("Work", "Call"))));

    [Fact]
    public void An_empty_list_copies_nothing() => Assert.Equal("", CopyText.Board(Board(Cat("Empty"))));

    [Fact]
    public void Counts_the_items_copied()
    {
        Assert.Equal(2, CopyText.Count(Board(Cat("A", "a", "b"), Cat("B"))));
        Assert.Equal("Copied 1 item", CopyText.Announcement(1));
        Assert.Equal("Copied 3 items", CopyText.Announcement(3));
    }
}
