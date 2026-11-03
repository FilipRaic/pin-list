using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Xunit;

namespace PinList.Tests;

/// <summary>The widget's "category color leads" look.</summary>
public class WidgetLookTests : UiTest
{
    static double CenterX(Control c, Visual root) =>
        c.TranslatePoint(new Point(c.Bounds.Width / 2, 0), root)!.Value.X;

    static Color BrushColor(IBrush? brush) => ((ISolidColorBrush)brush!).Color;

    [AvaloniaFact]
    public void Dot_and_guide_line_share_one_center()
    {
        var w = OpenNew(("Work", new[] { "one", "two" }));
        var dot = Find<Avalonia.Controls.Shapes.Ellipse>(w, e => e.Classes.Contains("catdot"))!;
        var guide = Find<Border>(w, b => b.Classes.Contains("guide"))!;
        Assert.True(guide.Bounds.Height > 20, "the line runs along the items");
        Assert.Equal(CenterX(dot, w), CenterX(guide, w), 0.5);
        Assert.Equal(2, guide.Bounds.Width);
    }

    [AvaloniaFact]
    public void Category_name_uses_the_readable_shade()
    {
        var board = MakeBoard(("Work", new[] { "a" }));
        board.Style = "Yellow";
        board.Categories[0].Color = "#F7C948";
        var w = Open(board);
        var name = Find<TextBlock>(w, t => t.Classes.Contains("catname"))!;
        Assert.Equal(Themes.CategoryShade("#F7C948", "Yellow"), BrushColor(name.Foreground));
        var pill = Find<Border>(w, b => b.Classes.Contains("countpill"))!;
        Assert.Equal(Themes.CategoryColor("#F7C948", "Yellow", CategoryPart.Pill), BrushColor(pill.Background));
        var check = Find<Button>(w, b => b.Classes.Contains("check"))!;
        Assert.Equal(Themes.CategoryColor("#F7C948", "Yellow", CategoryPart.Ring), BrushColor(check.BorderBrush));
        Assert.Equal(Themes.CategoryColor("#F7C948", "Yellow", CategoryPart.Fill), BrushColor(check.Foreground));
        // the tick sits on the fill, so it gets its own readable color (carried in Background)
        Assert.Equal(Themes.CategoryColor("#F7C948", "Yellow", CategoryPart.Tick), BrushColor(check.Background));
    }

    [AvaloniaFact]
    public void Changing_the_list_color_changes_the_category_shades()
    {
        var board = MakeBoard(("Work", new[] { "a" }));
        board.Categories[0].Color = "#F7C948";
        var w = Open(board);
        var name = Find<TextBlock>(w, t => t.Classes.Contains("catname"))!;
        Assert.Equal(Themes.CategoryShade("#F7C948", "Dark"), BrushColor(name.Foreground));
        board.Style = "Yellow";
        Pump();
        Assert.Equal(Themes.CategoryShade("#F7C948", "Yellow"), BrushColor(name.Foreground));
    }

    [AvaloniaFact]
    public void Category_colors_follow_high_contrast_live()
    {
        var w = OpenNew(("Work", new[] { "a" }));
        var name = Find<TextBlock>(w, t => t.Classes.Contains("catname"))!;
        var dot = Find<Avalonia.Controls.Shapes.Ellipse>(w, e => e.Classes.Contains("catdot"))!;
        Assert.NotEqual(Colors.White, BrushColor(name.Foreground));

        var hc = new ContrastColors(Colors.Black, Colors.White, Colors.Cyan, Colors.Navy, Colors.Yellow);
        Themes.ContrastSource = () => hc;
        App.RefreshAllWindows();
        Pump();
        Assert.Equal(Colors.White, BrushColor(name.Foreground));
        var guide = Find<Border>(w, b => b.Classes.Contains("guide"))!;
        Assert.Equal(Colors.White, BrushColor(guide.Background));
        var check = Find<Button>(w, b => b.Classes.Contains("check"))!;
        Assert.Equal(Colors.White, BrushColor(check.BorderBrush));
        Assert.Equal(Colors.Cyan, BrushColor(check.Foreground));
        // only the dot keeps the category's own color
        Assert.Equal(Color.Parse(w.Board.Categories[0].Color), BrushColor(dot.Fill));
    }

    [AvaloniaFact]
    public void Due_chips_are_pills_with_tinted_backgrounds()
    {
        var w = OpenNew(("Work", new[] { "a", "b", "c" }));
        var items = w.Board.Categories[0].Items;
        Actions.SetDueDate(items[0], DateTime.Today.AddDays(-2));
        Actions.SetDueDate(items[1], DateTime.Today);
        Actions.SetDueDate(items[2], DateTime.Today.AddDays(3));
        Pump();
        Border Chip(TodoItem i) => Find<Border>(Row(w, i), b => b.Classes.Contains("chipbox"))!;
        Assert.Equal(((ImmutableSolidColorBrush)w.Resources["OverdueBg"]!).Color, BrushColor(Chip(items[0]).Background));
        Assert.Equal(((ImmutableSolidColorBrush)w.Resources["AccentBg"]!).Color, BrushColor(Chip(items[1]).Background));
        Assert.Equal(((ImmutableSolidColorBrush)w.Resources["ChipBg"]!).Color, BrushColor(Chip(items[2]).Background));
    }

    [AvaloniaFact]
    public void The_header_is_44_high_with_a_line_under_it()
    {
        var w = OpenNew(("Work", new[] { "a" }));
        Assert.Equal(44, Named<Grid>(w, "Header").Bounds.Height);
        Assert.NotNull(Named<LineIcon>(w, "LockIcon").Data); // the lock is drawn as a line icon
    }

    // ================= Completing =================

    [AvaloniaFact]
    public void Completing_shows_the_undo_pill_and_undo_brings_it_back()
    {
        CompleteIn(TimeSpan.FromMilliseconds(100));
        var w = OpenNew(("Work", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];
        var a = cat.Items[0];
        Assert.False(Named<Border>(w, "UndoPill").IsVisible);

        Click(Find<Button>(Row(w, a), b => b.Classes.Contains("check"))!);
        Assert.True(a.IsCompleting);
        Assert.True(UiHost.WaitUntil(() => !cat.Items.Contains(a)));
        Assert.Single(Store.Data.Archive);

        var pill = Named<Border>(w, "UndoPill");
        Assert.True(pill.IsVisible);
        Assert.Equal("Completed “a”", Named<TextBlock>(w, "UndoText").Text);
        Click(Named<Button>(w, "UndoBtn"));
        Assert.False(pill.IsVisible);
        Assert.Equal(new[] { "a", "b" }, cat.Items.Select(i => i.Text).OrderBy(t => t));
        Assert.Empty(Store.Data.Archive);
        Assert.StartsWith("Undone", A11y.LastAnnouncement);
    }

    [AvaloniaFact]
    public void A_change_in_another_list_hides_the_undo_pill()
    {
        CompleteIn(TimeSpan.Zero);
        var w = OpenNew(("Work", new[] { "a" }));
        var other = OpenNew(("Home", new[] { "x" }));
        var a = w.Board.Categories[0].Items[0];

        Click(Find<Button>(Row(w, a), b => b.Classes.Contains("check"))!);
        var pill = Named<Border>(w, "UndoPill");
        Assert.True(pill.IsVisible);

        // its Undo would now undo the rename, not the completion
        Actions.RenameBoard(other.Board, "Renamed");
        Pump();
        Assert.False(pill.IsVisible);
        Assert.Single(Store.Data.Archive);
    }

    [AvaloniaFact]
    public void An_undo_history_change_off_the_UI_thread_hides_the_pill_safely()
    {
        CompleteIn(TimeSpan.Zero);
        var w = OpenNew(("Work", new[] { "a" }));
        Click(Find<Button>(Row(w, w.Board.Categories[0].Items[0]), b => b.Classes.Contains("check"))!);
        var pill = Named<Border>(w, "UndoPill");
        Assert.True(pill.IsVisible);

        System.Threading.Tasks.Task.Run(Actions.ClearUndo).Wait();
        Pump();
        Assert.False(pill.IsVisible);
    }

    [AvaloniaFact]
    public void The_undo_pill_goes_away_by_itself()
    {
        CompleteIn(TimeSpan.Zero);
        BoardWindow.UndoPillDuration = TimeSpan.FromMilliseconds(80);
        var w = OpenNew(("Work", new[] { "a" }));
        Click(Find<Button>(w, b => b.Classes.Contains("check"))!);
        var pill = Named<Border>(w, "UndoPill");
        Assert.True(pill.IsVisible);
        Assert.True(UiHost.WaitUntil(() => !pill.IsVisible));
    }

    [AvaloniaFact]
    public void The_row_leaves_before_it_is_archived()
    {
        CompleteIn(TimeSpan.FromMilliseconds(400));
        var w = OpenNew(("Work", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];
        var a = cat.Items[0];
        Click(Find<Button>(Row(w, a), b => b.Classes.Contains("check"))!);
        Assert.False(a.IsLeaving);
        Assert.True(UiHost.WaitUntil(() => a.IsLeaving));
        Assert.Contains(a, cat.Items); // still there while it slides away
        var collapser = Find<Collapser>(w, c => c.Classes.Contains("leaving"));
        Assert.NotNull(collapser);
        Assert.True(UiHost.WaitUntil(() => !cat.Items.Contains(a)));
        Assert.False(a.IsLeaving);
    }

    [AvaloniaFact]
    public void Several_items_can_be_completed_quickly_in_a_row()
    {
        CompleteIn(TimeSpan.FromMilliseconds(120));
        var w = OpenNew(("Work", new[] { "a", "b", "c" }));
        var cat = w.Board.Categories[0];
        foreach (var item in cat.Items.ToList()) Click(Find<Button>(Row(w, item), b => b.Classes.Contains("check"))!);
        Assert.True(UiHost.WaitUntil(() => cat.Items.Count == 0));
        Assert.Equal(3, Store.Data.Archive.Count);
    }

    [AvaloniaFact]
    public void The_menu_complete_follows_the_same_steps()
    {
        CompleteIn(TimeSpan.FromMilliseconds(100));
        var w = OpenNew(("Work", new[] { "a" }));
        var a = w.Board.Categories[0].Items[0];
        RightClick(Row(w, a));
        ChooseFromMenu(w.LastMenu!, "Complete");
        Assert.True(a.IsCompleting);
        Assert.True(UiHost.WaitUntil(() => Store.Data.Archive.Count == 1));
    }

    [AvaloniaFact]
    public void A_widget_that_goes_away_while_an_item_completes_still_archives_it_quietly()
    {
        CompleteIn(TimeSpan.FromMilliseconds(100));
        var w = OpenNew(("Work", new[] { "a", "b" }));
        var cat = w.Board.Categories[0];
        Click(Find<Button>(Row(w, cat.Items[0]), b => b.Classes.Contains("check"))!);
        Actions.SetWidget(w.Board, false); // "Remove from desktop" while the row is crossed out
        Assert.True(UiHost.WaitUntil(() => cat.Items.Count == 1));
        Assert.Single(Store.Data.Archive);
    }

    [AvaloniaFact]
    public void The_count_pill_bumps_when_an_item_leaves()
    {
        CompleteIn(TimeSpan.Zero);
        var w = OpenNew(("Work", new[] { "a", "b" }));
        var pill = Find<Border>(w, b => b.Classes.Contains("countpill"))!;
        Click(Find<Button>(w, b => b.Classes.Contains("check"))!);
        Assert.Contains("bump", pill.Classes);
        Assert.Equal("1", Find<TextBlock>(pill)!.Text);
        Assert.True(UiHost.WaitUntil(() => !pill.Classes.Contains("bump")));
    }
}
