using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PinList;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace PinList.Tests;

public class ConfirmDialogTests : UiTest
{
    static List<Button> Buttons(ConfirmDialog d) => FindAll<Button>(d).ToList();

    [Fact]
    public void Clicking_a_button_returns_its_index()
    {
        int result = Ui(() =>
        {
            WhenOpened<ConfirmDialog>(d =>
            {
                Assert.Equal("Delete \"Work\"?", d.Title); // screen readers announce the title
                Assert.Equal("Items are gone for good.", AutomationProperties.GetHelpText(d));
                var b = Buttons(d);
                Assert.Equal(new object[] { "Cancel", "Keep", "Delete" }, b.Select(x => x.Content));
                Assert.True(b[2].IsDefault);
                Assert.True(b[2].IsKeyboardFocused); // focus starts on the default button
                Click(b[1]);
            });
            return ConfirmDialog.Choose(null, "Delete \"Work\"?", "Items are gone for good.", 2,
                ("Cancel", DialogButtonKind.Normal), ("Keep", DialogButtonKind.Accent), ("Delete", DialogButtonKind.Danger));
        });
        Assert.Equal(1, result);
    }

    [Fact]
    public void Esc_returns_minus_one()
    {
        int result = Ui(() =>
        {
            WhenOpened<ConfirmDialog>(d => Press(d, Key.Escape));
            return ConfirmDialog.Choose(null, "Title", "", 0, ("OK", DialogButtonKind.Accent));
        });
        Assert.Equal(-1, result);
    }

    [Fact]
    public void Opens_over_its_owner_and_hides_an_empty_message()
    {
        int result = Ui(() =>
        {
            var owner = OpenNew(("A", new string[0]));
            WhenOpened<ConfirmDialog>(d =>
            {
                Assert.Same(owner, d.Owner);
                Assert.Equal(Visibility.Collapsed, Named<TextBlock>(d, "MessageText").Visibility);
                // dragging only starts while the button is really held; a simulated click must not break anything
                MouseDown(d);
                Click(Buttons(d)[0]);
            });
            return new WindowsDialogs().Choose(owner, "Sure?", "", 1, ("Cancel", DialogButtonKind.Normal), ("Delete", DialogButtonKind.Danger));
        });
        Assert.Equal(0, result);
    }

    [Fact]
    public void Text_size_makes_the_dialog_wider()
    {
        Ui(() =>
        {
            A11y.TextScaleSource = () => 1.5;
            A11y.RefreshTextScale();
            WhenOpened<ConfirmDialog>(d =>
            {
                Assert.Equal(380 * 1.5, d.Width);
                Assert.IsType<System.Windows.Media.ScaleTransform>(((FrameworkElement)d.Content).LayoutTransform);
                Press(d, Key.Escape);
            });
            ConfirmDialog.Choose(null, "T", "M", 0, ("OK", DialogButtonKind.Accent));
        });
    }
}

public class DialogsHelperTests : UiTest
{
    [Fact]
    public void Ask_is_true_only_for_the_second_button()
    {
        Ui(() =>
        {
            Dialogs.ChooseAnswer = _ => 1;
            Assert.True(PinList.Dialogs.Ask(null, "T", "M", "Delete"));
            Dialogs.ChooseAnswer = _ => 0;
            Assert.False(PinList.Dialogs.Ask(null, "T", "M", "Delete"));
            PinList.Dialogs.Info(null, "Info", "Text");
            Assert.Equal(new[] { "T: M", "T: M", "Info: Text" }, Dialogs.Shown);
        });
    }
}

public class DateDialogTests : UiTest
{
    static readonly DateTime Morning = new(2030, 6, 12, 10, 15, 30); // a Wednesday

    public DateDialogTests() => Ui(() => DateDialog.Clock = () => Morning);

    static List<Button> Quick(DateDialog d) => Named<WrapPanel>(d, "QuickPanel").Children.OfType<Button>().ToList();
    static Calendar Cal(DateDialog d) => Named<Calendar>(d, "Cal");
    static TextBox Time(DateDialog d) => Named<TextBox>(d, "TimeBox");
    static TextBlock Error(DateDialog d) => Named<TextBlock>(d, "ErrorText");
    static Button Save(DateDialog d) => Named<Button>(d, "SaveButton");

    /// <summary>Esc works through the Cancel button (IsCancel), which only real key presses trigger; click it instead.</summary>
    static void Cancel(DateDialog d) => Click(FindAll<Button>(d).First(b => b.IsCancel));

    DateTime? PickDue(DateTime? current, Action<DateDialog> act) => Ui(() =>
    {
        var owner = OpenNew(("A", new string[0]));
        WhenOpened(act);
        return new WindowsDialogs().PickDueDate(owner, current);
    });

    DateTime? PickReminder(DateTime? current, DateTime? due, Action<DateDialog> act) => Ui(() =>
    {
        var owner = OpenNew(("A", new string[0]));
        WhenOpened(act);
        return new WindowsDialogs().PickReminder(owner, current, due);
    });

    [Theory]
    [InlineData(0, "2030-06-12")] // Today
    [InlineData(1, "2030-06-13")] // Tomorrow
    [InlineData(2, "2030-06-17")] // Next week = next Monday
    public void Due_date_quick_choices(int button, string expected)
    {
        var result = PickDue(null, d =>
        {
            Assert.Equal("Add due date", d.Title);
            Assert.Equal(new object[] { "Today", "Tomorrow", "Next week" }, Quick(d).Select(b => b.Content));
            Assert.Equal(Visibility.Collapsed, Named<Grid>(d, "TimeRow").Visibility);
            Assert.True(Cal(d).IsKeyboardFocusWithin);
            Click(Quick(d)[button]);
        });
        Assert.Equal(DateTime.Parse(expected), result);
    }

    [Fact]
    public void Next_week_on_a_Monday_is_the_Monday_after()
    {
        Ui(() => DateDialog.Clock = () => new DateTime(2030, 6, 17, 9, 0, 0));
        var result = PickDue(null, d => Click(Quick(d)[2]));
        Assert.Equal(new DateTime(2030, 6, 24), result);
    }

    [Fact]
    public void Picking_a_day_in_the_calendar_and_saving()
    {
        var result = PickDue(new DateTime(2030, 7, 1), d =>
        {
            Assert.Equal("Change due date", d.Title);
            Assert.Equal(new DateTime(2030, 7, 1), Cal(d).SelectedDate);
            Cal(d).SelectedDate = new DateTime(2030, 7, 4);
            // the calendar keeps the mouse captured after a click; releasing it must not fail
            Cal(d).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseUpEvent });
            MouseDown(d); // dragging the dialog: only with the button really held
            Click(Save(d));
        });
        Assert.Equal(new DateTime(2030, 7, 4), result);
    }

    [Fact]
    public void An_overdue_date_starts_on_today_and_past_days_are_blocked()
    {
        var result = PickDue(new DateTime(2030, 6, 1), d =>
        {
            Assert.Equal(Morning.Date, Cal(d).SelectedDate);
            Assert.True(Cal(d).BlackoutDates.Contains(Morning.Date.AddDays(-1)));
            Cancel(d);
        });
        Assert.Null(result);
    }

    [Fact]
    public void Saving_without_a_day_shows_an_error()
    {
        var result = PickDue(null, d =>
        {
            Cal(d).SelectedDate = null;
            Click(Save(d));
            Assert.Equal("Pick a day.", Error(d).Text);
            Assert.Equal(Visibility.Visible, Error(d).Visibility);
            Cancel(d);
        });
        Assert.Null(result);
    }

    [Fact]
    public void Reminder_quick_choices_in_the_morning()
    {
        var result = PickReminder(null, new DateTime(2030, 6, 20), d =>
        {
            Assert.Equal("Add reminder", d.Title);
            Assert.Equal(new object[] { "In 1 hour", "This evening", "Tomorrow 9:00", "Thu 20 Jun 9:00" }, Quick(d).Select(b => b.Content));
            Assert.Equal(Visibility.Visible, Named<Grid>(d, "TimeRow").Visibility);
            Assert.True(Time(d).IsKeyboardFocused);
            Assert.Equal(new DateTime(2030, 6, 20), Cal(d).SelectedDate); // starts on the due date
            Click(Quick(d)[0]);
        });
        Assert.Equal(new DateTime(2030, 6, 12, 11, 15, 0), result); // seconds dropped
    }

    [Theory]
    [InlineData(1, "2030-06-12 18:00")]
    [InlineData(2, "2030-06-13 09:00")]
    [InlineData(3, "2030-06-20 09:00")]
    public void Other_reminder_quick_choices(int button, string expected)
    {
        var result = PickReminder(null, new DateTime(2030, 6, 20), d => Click(Quick(d)[button]));
        Assert.Equal(DateTime.Parse(expected), result);
    }

    [Fact]
    public void In_the_evening_there_is_no_this_evening_and_no_chip_for_tomorrow_or_past_due_dates()
    {
        Ui(() => DateDialog.Clock = () => new DateTime(2030, 6, 12, 20, 0, 0));
        PickReminder(null, new DateTime(2030, 6, 13), d =>
        {
            Assert.Equal(new object[] { "In 1 hour", "Tomorrow 9:00" }, Quick(d).Select(b => b.Content));
            Cancel(d);
        });
        PickReminder(null, new DateTime(2030, 6, 1), d =>
        {
            Assert.Equal(new object[] { "In 1 hour", "Tomorrow 9:00" }, Quick(d).Select(b => b.Content));
            Cancel(d);
        });
    }

    [Fact]
    public void Typing_a_time_and_saving()
    {
        var result = PickReminder(new DateTime(2030, 6, 14, 8, 30, 0), null, d =>
        {
            Assert.Equal("Change reminder", d.Title);
            Assert.Equal("08:30", Time(d).Text);
            Time(d).Text = "17.45";
            Click(Save(d));
        });
        Assert.Equal(new DateTime(2030, 6, 14, 17, 45, 0), result);
    }

    [Fact]
    public void Invalid_and_past_times_are_explained()
    {
        var result = PickReminder(null, null, d =>
        {
            Time(d).Text = "25:00";
            Assert.Equal(Visibility.Collapsed, Error(d).Visibility); // not while typing…
            Click(Save(d));
            Assert.Equal("Enter a time like 9:30 or 17:00.", Error(d).Text); // …only on Save

            Time(d).Text = "9:00"; // today 9:00 is before 10:15
            Click(Save(d));
            Assert.Equal("That time has already passed.", Error(d).Text);

            Time(d).Text = "11:00";
            Assert.Equal(Visibility.Collapsed, Error(d).Visibility); // fixed while typing: the error goes away
            Cancel(d);
        });
        Assert.Null(result);
    }
}

/// <summary>The Windows file dialogs, driven through their window messages.</summary>
public class FileDialogTests : UiTest
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint threadId, EnumProc proc, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, string l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);

    const uint WM_CLOSE = 0x0010, WM_SETTEXT = 0x000C, WM_COMMAND = 0x0111;

    static IntPtr FindFileDialog()
    {
        IntPtr found = IntPtr.Zero;
        EnumThreadWindows(GetCurrentThreadId(), (h, _) =>
        {
            var name = new StringBuilder(64);
            GetClassName(h, name, 64);
            if (name.ToString() == "#32770" && IsWindowVisible(h)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Waits (inside the dialog's own message loop) for the file dialog, then acts on it.</summary>
    static void WhenFileDialogOpens(Action<IntPtr> act)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var deadline = DateTime.UtcNow.AddSeconds(15);
        timer.Tick += (_, _) =>
        {
            var dlg = FindFileDialog();
            if (dlg == IntPtr.Zero && DateTime.UtcNow < deadline) return;
            timer.Stop();
            if (dlg != IntPtr.Zero) act(dlg);
        };
        timer.Start();
    }

    [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr dlg, int id);

    static void TypeFileNameAndOk(IntPtr dlg, string path)
    {
        // the file name box has control id 1148 (a combo box with an edit field inside, or the edit field itself)
        IntPtr box = GetDlgItem(dlg, 1148), edit = box;
        EnumChildWindows(box, (h, _) =>
        {
            var name = new StringBuilder(64);
            GetClassName(h, name, 64);
            if (name.ToString() == "Edit") { edit = h; return false; }
            return true;
        }, IntPtr.Zero);
        SendMessage(edit, WM_SETTEXT, IntPtr.Zero, path);
        PostMessage(dlg, WM_COMMAND, (IntPtr)1 /* IDOK */, IntPtr.Zero);
    }

    [Fact]
    public void Save_dialog_returns_the_chosen_path_or_null()
    {
        var target = Path.Combine(Dir, "export.json");
        Directory.CreateDirectory(Dir);
        Ui(() =>
        {
            WhenFileDialogOpens(dlg => PostMessage(dlg, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
            Assert.Null(new WindowsDialogs().SaveFile(null, "Export", "x.json", "JSON|*.json"));

            // the suggested name can be a full path; confirming it returns that path
            WhenFileDialogOpens(dlg => PostMessage(dlg, WM_COMMAND, (IntPtr)1 /* IDOK */, IntPtr.Zero));
            Assert.Equal(target, new WindowsDialogs().SaveFile(null, "Export", target, "JSON|*.json"));
        });
    }

    [Fact]
    public void Open_dialog_returns_the_chosen_path_or_null()
    {
        Directory.CreateDirectory(Dir);
        var file = Path.Combine(Dir, "import.json");
        File.WriteAllText(file, "{}");
        Ui(() =>
        {
            WhenFileDialogOpens(dlg => PostMessage(dlg, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
            Assert.Null(new WindowsDialogs().OpenFile(null, "Import", "JSON|*.json"));

            WhenFileDialogOpens(dlg => TypeFileNameAndOk(dlg, file));
            Assert.Equal(file, new WindowsDialogs().OpenFile(null, "Import", "JSON|*.json"));
        });
    }
}
