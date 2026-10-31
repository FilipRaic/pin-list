using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>What screen readers hear for items, categories and undo.</summary>
public class AccessibilityTests : StoreTestBase
{
    [Fact]
    public void Item_name_includes_due_date_and_reminder()
    {
        var item = new TodoItem { Text = "Send the invoice" };
        Assert.Equal("Send the invoice", item.AccessibleName);

        item.DueDate = DateTime.Today;
        Assert.Equal("Send the invoice, due Today", item.AccessibleName);

        item.ReminderAt = DateTime.Today.AddDays(1).AddHours(9);
        Assert.Equal("Send the invoice, due Today, reminder Tomorrow 09:00", item.AccessibleName);

        item.ReminderShown = true; // a reminder that already went off isn't mentioned
        Assert.Equal("Send the invoice, due Today", item.AccessibleName);
    }

    [Fact]
    public void Overdue_items_say_so()
    {
        var item = new TodoItem { Text = "Renew passport", DueDate = DateTime.Today.AddDays(-1) };
        Assert.Equal("Renew passport, overdue, was due Yesterday", item.AccessibleName);
    }

    [Fact]
    public void Multi_line_items_are_read_as_one_line()
    {
        var item = new TodoItem { Text = "Shopping\nmilk\neggs" };
        Assert.Equal("Shopping, milk, eggs", item.AccessibleName);
        Assert.Equal(item.AccessibleName, item.ToString()); // ItemsControl uses ToString for list items
    }

    [Fact]
    public void Item_name_change_is_announced_to_bindings()
    {
        var item = new TodoItem { Text = "a" };
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.Text = "b";
        item.DueDate = DateTime.Today;

        Assert.Equal(2, changed.Count(n => n == nameof(TodoItem.AccessibleName)));
    }

    [Fact]
    public void Category_name_has_item_count_and_collapsed_state()
    {
        var cat = new Category { Name = "Work" };
        var changes = 0;
        cat.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Category.AccessibleName)) changes++; };

        Assert.Equal("Work, 0 items", cat.AccessibleName);
        cat.Items.Add(new TodoItem { Text = "x" });
        Assert.Equal("Work, 1 item", cat.AccessibleName);
        cat.Collapsed = true;
        Assert.Equal("Work, 1 item, collapsed", cat.AccessibleName);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Category_count_follows_a_replaced_items_collection()
    {
        // loading from JSON replaces the collection; the count must still update
        var cat = new Category { Name = "Work", Items = new() };
        cat.Items.Add(new TodoItem());
        cat.Items.Add(new TodoItem());
        Assert.Equal("Work, 2 items", cat.AccessibleName);
    }

    [Fact]
    public void Window_title_falls_back_to_the_app_name()
    {
        Assert.Equal("Groceries – PinList", new Board { Title = "Groceries" }.WindowTitle);
        Assert.Equal("PinList", new Board { Title = "" }.WindowTitle);
    }

    [Fact]
    public void Undo_says_what_was_undone()
    {
        var cat = MakeBoard(("A", new[] { "Send the invoice" })).Categories[0];
        Actions.Complete(cat.Items[0]);

        Assert.True(Actions.Undo(out var what));
        Assert.Equal("completing “Send the invoice”", what);

        Assert.False(Actions.Undo(out what));
        Assert.Equal("", what);
    }

    [Fact]
    public void Long_texts_are_shortened_in_undo_descriptions()
    {
        var cat = MakeBoard(("A", new[] { new string('x', 100) + "\nsecond line" })).Categories[0];
        Actions.Delete(cat.Items[0]);
        Actions.Undo(out var what);
        Assert.Equal("deleting “" + new string('x', 60) + "…”", what);
    }
}
