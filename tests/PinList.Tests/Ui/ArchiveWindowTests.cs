using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PinList;
using Xunit;
using Button = System.Windows.Controls.Button;
using ListView = System.Windows.Controls.ListView;
using TextBox = System.Windows.Controls.TextBox;

namespace PinList.Tests;

public class ArchiveWindowTests : UiTest
{
    static ListView List(ArchiveWindow a) => Named<ListView>(a, "List");
    static TextBox Search(ArchiveWindow a) => Named<TextBox>(a, "SearchBox");
    static TextBlock Status(ArchiveWindow a) => Named<TextBlock>(a, "StatusText");
    static Button ButtonWith(ArchiveWindow a, string text) => FindAll<Button>(a).First(b => (string)b.Content == text);

    /// <summary>A widget whose items are all completed (archive: newest first).</summary>
    static Board Completed(params string[] texts)
    {
        var board = MakeBoard(("Work", texts));
        foreach (var i in board.Categories[0].Items.ToList()) Actions.Complete(i);
        Actions.ClearUndo();
        return board;
    }

    /// <summary>
    /// A double click on the list that started on <paramref name="origin"/> (MouseDoubleClick doesn't bubble,
    /// so it is raised on the list itself, with where it started as its original source).
    /// </summary>
    static void DoubleClick(ListView list, DependencyObject origin)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent };
        typeof(RoutedEventArgs).GetField("_originalSource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(args, origin);
        list.RaiseEvent(args);
        Pump();
    }

    static ArchiveWindow OpenArchive()
    {
        App.ShowArchive();
        Pump();
        return App.Archive!;
    }

    [Fact]
    public void Lists_completed_items_and_starts_on_the_first()
    {
        Ui(() =>
        {
            Completed("a", "b");
            var a = OpenArchive();
            Assert.Equal("2 items", Named<TextBlock>(a, "CountText").Text);
            Assert.Equal(Visibility.Collapsed, Named<TextBlock>(a, "EmptyText").Visibility);
            Assert.Equal(0, List(a).SelectedIndex);
            Assert.IsType<ListViewItem>(Keyboard.FocusedElement);
            Assert.Equal("Completed items", AutomationProperties.GetName(List(a)));
            Assert.StartsWith("b, completed", List(a).Items[0]!.ToString()); // read as text, not a type name
        });
    }

    [Fact]
    public void An_empty_archive_starts_in_the_search_box()
    {
        Ui(() =>
        {
            var a = OpenArchive();
            Assert.Equal("0 items", Named<TextBlock>(a, "CountText").Text);
            Assert.Equal(Visibility.Visible, Named<TextBlock>(a, "EmptyText").Visibility);
            Assert.Same(Search(a), Keyboard.FocusedElement);

            Completed("only");
            Assert.Equal("1 item", Named<TextBlock>(a, "CountText").Text); // follows changes
        });
    }

    [Fact]
    public void Opening_it_again_brings_back_the_same_window()
    {
        Ui(() =>
        {
            var first = OpenArchive();
            first.WindowState = WindowState.Minimized;
            Assert.Same(first, OpenArchive());
            Assert.Equal(WindowState.Normal, first.WindowState);
            first.Close();
            Assert.Null(App.Archive);
        });
    }

    [Fact]
    public void Search_filters_by_text_category_and_widget()
    {
        Ui(() =>
        {
            var board = Completed("Buy milk", "Call mum");
            var a = OpenArchive();
            Search(a).Text = "milk";
            Assert.Single(List(a).Items);
            Search(a).Text = "WORK"; // category, any case
            Assert.Equal(2, List(a).Items.Count);
            Search(a).Text = board.Title;
            Assert.Equal(2, List(a).Items.Count);
            Search(a).Text = "nothing like this";
            Assert.Empty(List(a).Items);
        });
    }

    [Fact]
    public void Keyboard_shortcuts()
    {
        Ui(() =>
        {
            Completed("a");
            var a = OpenArchive();

            Press(a, Key.F, ModifierKeys.Control);
            Assert.Same(Search(a), Keyboard.FocusedElement);

            Search(a).Text = "zzz";
            Press(Search(a), Key.Escape); // clears the search first…
            Assert.Equal("", Search(a).Text);
            Press(Search(a), Key.Escape); // …then closes
            Assert.Null(App.Archive);
        });
    }

    [Fact]
    public void Restore_with_Enter_double_click_or_button_and_undo()
    {
        Ui(() =>
        {
            var board = Completed("a", "b", "c"); // archive: c, b, a
            var cat = board.Categories[0];
            var a = OpenArchive();

            Press(List(a), Key.Enter); // c
            Assert.Equal(new[] { "c" }, cat.Items.Select(i => i.Text));
            Assert.Equal("Restored. Ctrl+Z undoes.", Status(a).Text);
            Assert.Equal(0, List(a).SelectedIndex); // the next row is selected, so Enter can be pressed again
            Assert.IsType<ListViewItem>(Keyboard.FocusedElement);

            var row = (ListViewItem)List(a).ItemContainerGenerator.ContainerFromIndex(0)!;
            DoubleClick(List(a), Find<TextBlock>(row)!);
            Assert.Equal(2, cat.Items.Count); // b

            List(a).SelectAll();
            Click(ButtonWith(a, "Restore"));
            Assert.Equal(3, cat.Items.Count);
            Assert.Equal("Restored. Ctrl+Z undoes.", Status(a).Text); // the last one (a)
            Assert.Same(Search(a), Keyboard.FocusedElement); // the archive is empty now

            Press(a, Key.Z, ModifierKeys.Control);
            Assert.Single(Store.Data.Archive);
        });
    }

    [Fact]
    public void Restoring_several_items_says_how_many()
    {
        Ui(() =>
        {
            Completed("a", "b");
            var a = OpenArchive();
            List(a).SelectAll();
            Click(ButtonWith(a, "Restore"));
            Assert.Equal("Restored 2 items. Ctrl+Z undoes.", Status(a).Text);
        });
    }

    [Fact]
    public void Double_click_outside_a_row_does_nothing()
    {
        Ui(() =>
        {
            Completed("a");
            var a = OpenArchive();
            DoubleClick(List(a), List(a));
            Assert.Single(Store.Data.Archive);
        });
    }

    [Fact]
    public void Restoring_into_a_deleted_category_says_where_it_went()
    {
        Ui(() =>
        {
            var gone = Completed("x", "y", "z");
            var keep = MakeBoard(("Home", new string[0]));
            Store.Data.Boards.Remove(gone);
            var a = OpenArchive();

            List(a).SelectedIndex = 0;
            Click(ButtonWith(a, "Restore"));
            Assert.Equal("\"Work\" no longer exists, so the item went to \"Home\".", Status(a).Text);

            List(a).SelectAll(); // the other two
            Click(ButtonWith(a, "Restore"));
            Assert.Contains("items went to \"Home\" because their category no longer exists.", Status(a).Text);
            Assert.Equal(3, keep.Categories[0].Items.Count);
        });
    }

    [Fact]
    public void Restoring_with_no_category_anywhere_explains_it()
    {
        Ui(() =>
        {
            var board = Completed("x");
            Store.Data.Boards.Remove(board);
            var a = OpenArchive();
            Click(ButtonWith(a, "Restore"));
            Assert.Equal("There is no category to restore into. Add a category to a widget first.", Status(a).Text);

            List(a).SelectedItems.Clear();
            Click(ButtonWith(a, "Restore")); // nothing selected: nothing happens
        });
    }

    [Fact]
    public void The_status_message_goes_away_after_a_while()
    {
        Ui(() =>
        {
            Completed("a", "b");
            var a = OpenArchive();
            var timer = (DispatcherTimer)typeof(ArchiveWindow).GetField("_statusTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(a)!;
            timer.Interval = TimeSpan.FromMilliseconds(50);
            Press(List(a), Key.Enter);
            Assert.Equal(Visibility.Visible, Status(a).Visibility);
            Assert.Equal(Visibility.Collapsed, Named<TextBlock>(a, "HintText").Visibility);
            UiHost.Wait(TimeSpan.FromMilliseconds(200));
            Assert.Equal(Visibility.Collapsed, Status(a).Visibility);
            Assert.Equal(Visibility.Visible, Named<TextBlock>(a, "HintText").Visibility);
        });
    }

    [Fact]
    public void Delete_removes_permanently_and_undo_brings_back()
    {
        Ui(() =>
        {
            Completed("a", "b", "c");
            var a = OpenArchive();
            List(a).SelectedIndex = 2;
            Press(List(a), Key.Delete);
            Assert.Equal(2, Store.Data.Archive.Count);
            Assert.Equal(1, List(a).SelectedIndex); // the next row (now the last one)

            List(a).SelectAll();
            Click(ButtonWith(a, "Delete permanently"));
            Assert.Empty(Store.Data.Archive);
            Assert.Same(Search(a), Keyboard.FocusedElement);

            Click(ButtonWith(a, "Delete permanently")); // nothing selected: nothing happens

            Search(a).Focus();
            Press(Search(a), Key.Z, ModifierKeys.Control); // empty search box: undo works
            Assert.Equal(2, Store.Data.Archive.Count);

            Search(a).Text = "typing";
            Press(Search(a), Key.Z, ModifierKeys.Control); // while typing, Ctrl+Z belongs to the text
            Assert.Equal(2, Store.Data.Archive.Count);
            Press(Search(a), Key.A); // other keys are left alone
        });
    }

    [Fact]
    public void Follows_the_theme_and_the_text_size()
    {
        Ui(() =>
        {
            A11y.TextScaleSource = () => 1.5;
            A11y.RefreshTextScale();
            var a = OpenArchive();
            Assert.Equal(480 * 1.5, a.MinWidth);
            Assert.IsType<System.Windows.Media.ScaleTransform>(((FrameworkElement)a.Content).LayoutTransform);

            App.SetTheme("Light"); // the title bar is restyled through ThemeChanged
            Assert.Equal("Light", Store.Data.Settings.Theme);
            a.Close();
            App.SetTheme("Dark"); // a closed archive no longer listens
        });
    }
}
