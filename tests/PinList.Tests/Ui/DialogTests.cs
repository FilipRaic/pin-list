using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Xunit;

namespace PinList.Tests;

/// <summary>Shows a real dialog and lets the test answer it while it's open.</summary>
public abstract class DialogTest : UiTest
{
    /// <summary>Starts the dialog, runs <paramref name="act"/> on it once it's open, and returns its result.</summary>
    protected static T Run<T, TDialog>(Func<Task<T>> show, Action<TDialog> act) where TDialog : Window
    {
        var task = show();
        Pump();
        var dlg = Assert.IsType<TDialog>(DialogHost.Current);
        Assert.True(dlg.IsVisible);
        act(dlg);
        Pump();
        Assert.True(task.IsCompleted, "the dialog is still open");
        Assert.Null(DialogHost.Current);
        return task.Result;
    }
}

public class ConfirmDialogTests : DialogTest
{
    static List<Button> Buttons(ConfirmDialog d) => d.ButtonPanel.Children.OfType<Button>().ToList();

    [AvaloniaFact]
    public void Clicking_a_button_returns_its_index()
    {
        int result = Run<int, ConfirmDialog>(
            () => ConfirmDialog.Choose(null, "Delete \"Work\"?", "Items are gone for good.", 2,
                ("Cancel", DialogButtonKind.Normal), ("Keep", DialogButtonKind.Accent), ("Delete", DialogButtonKind.Danger)),
            d =>
            {
                Assert.Equal("Delete \"Work\"?", d.Title); // screen readers announce the title
                Assert.Equal("Items are gone for good.", AutomationProperties.GetHelpText(d));
                var b = Buttons(d);
                Assert.Equal(new object[] { "Cancel", "Keep", "Delete" }, b.Select(x => x.Content!));
                Assert.True(b[2].IsDefault);
                Assert.Contains("danger", b[2].Classes);
                Assert.Contains("accent", b[1].Classes);
                Assert.True(b[2].IsFocused); // focus starts on the default button
                Click(b[1]);
            });
        Assert.Equal(1, result);
    }

    [AvaloniaFact]
    public void Esc_returns_minus_one()
    {
        int result = Run<int, ConfirmDialog>(
            () => ConfirmDialog.Choose(null, "Title", "", 0, ("OK", DialogButtonKind.Accent)),
            d => Press(d, Key.Escape));
        Assert.Equal(-1, result);
    }

    [AvaloniaFact]
    public void Opens_over_its_owner_and_hides_an_empty_message()
    {
        var owner = OpenNew(("A", new string[0]));
        int result = Run<int, ConfirmDialog>(
            () => new AppDialogs().Choose(owner, "Sure?", "", 1, ("Cancel", DialogButtonKind.Normal), ("Delete", DialogButtonKind.Danger)),
            d =>
            {
                Assert.Same(owner, d.Owner);
                Assert.False(Named<TextBlock>(d, "MessageText").IsVisible);
                MouseDown(d); // dragging needs the real mouse; a simulated press must not break anything
                Click(Buttons(d)[0]);
            });
        Assert.Equal(0, result);
    }

    [AvaloniaFact]
    public void Text_size_makes_the_dialog_wider()
    {
        A11y.TextScaleSource = () => 1.5;
        A11y.RefreshTextScale();
        Run<int, ConfirmDialog>(
            () => ConfirmDialog.Choose(null, "T", "M", 0, ("OK", DialogButtonKind.Accent)),
            d =>
            {
                Assert.Equal(380 * 1.5, d.Width);
                Assert.IsType<ScaleTransform>(Named<LayoutTransformControl>(d, "Scaler").LayoutTransform);
                Press(d, Key.Escape);
            });
    }

    [AvaloniaFact]
    public void Keys_other_than_Esc_leave_the_dialog_open()
    {
        int result = Run<int, ConfirmDialog>(
            () => ConfirmDialog.Choose(null, "Title", "", 0, ("OK", DialogButtonKind.Accent)),
            d =>
            {
                Press(d, Key.A);
                Assert.True(d.IsVisible);
                Press(d, Key.Escape);
            });
        Assert.Equal(-1, result);
    }
}

public class DialogsHelperTests : UiTest
{
    [AvaloniaFact]
    public void Ask_is_true_only_for_the_second_button()
    {
        Dialogs.ChooseAnswer = _ => 1;
        Assert.True(PinList.Dialogs.Ask(null, "T", "M", "Delete").Result);
        Dialogs.ChooseAnswer = _ => 0;
        Assert.False(PinList.Dialogs.Ask(null, "T", "M", "Delete").Result);
        PinList.Dialogs.Info(null, "Info", "Text").Wait();
        Assert.Equal(new[] { "T: M", "T: M", "Info: Text" }, Dialogs.Shown);
    }

    [AvaloniaFact]
    public void File_pickers_without_a_choice_return_null()
    {
        var w = OpenNew(("A", new string[0]));
        var dialogs = new AppDialogs();
        var save = dialogs.SaveFile(w, "Export", "x.json");
        var open = dialogs.OpenFile(null, "Import"); // without an owner any PinList window is used
        UiHost.WaitUntil(() => save.IsCompleted && open.IsCompleted);
        Assert.Null(save.Result); // the headless platform has no file system dialogs
        Assert.Null(open.Result);
    }
}

public class DateDialogTests : DialogTest
{
    static readonly DateTime Morning = new(2030, 6, 12, 10, 15, 30); // a Wednesday

    public DateDialogTests() => DateDialog.Clock = () => Morning;

    static List<Button> Quick(DateDialog d) => d.Quick.Children.OfType<Button>().ToList();
    static void Cancel(DateDialog d) => Click(FindAll<Button>(d).First(b => b.IsCancel));

    DateTime? PickDue(DateTime? current, Action<DateDialog> act)
    {
        var owner = OpenNew(("A", new string[0]));
        return Run(() => new AppDialogs().PickDueDate(owner, current), act);
    }

    DateTime? PickReminder(DateTime? current, DateTime? due, Action<DateDialog> act)
    {
        var owner = OpenNew(("A", new string[0]));
        return Run(() => new AppDialogs().PickReminder(owner, current, due), act);
    }

    [AvaloniaTheory]
    [InlineData(0, "2030-06-12")] // Today
    [InlineData(1, "2030-06-13")] // Tomorrow
    [InlineData(2, "2030-06-17")] // Next week = next Monday
    public void Due_date_quick_choices(int button, string expected)
    {
        var result = PickDue(null, d =>
        {
            Assert.Equal("Add due date", d.Title);
            Assert.Equal(new object[] { "Today", "Tomorrow", "Next week" }, Quick(d).Select(b => b.Content!));
            Assert.False(Named<Grid>(d, "TimeRow").IsVisible);
            Assert.True(d.Calendar.IsKeyboardFocusWithin || d.Calendar.IsFocused);
            Click(Quick(d)[button]);
        });
        Assert.Equal(DateTime.Parse(expected), result);
    }

    [AvaloniaFact]
    public void Next_week_on_a_Monday_is_the_Monday_after()
    {
        DateDialog.Clock = () => new DateTime(2030, 6, 17, 9, 0, 0);
        var result = PickDue(null, d => Click(Quick(d)[2]));
        Assert.Equal(new DateTime(2030, 6, 24), result);
    }

    [AvaloniaFact]
    public void Picking_a_day_in_the_calendar_and_saving()
    {
        var result = PickDue(new DateTime(2030, 7, 1), d =>
        {
            Assert.Equal("Change due date", d.Title);
            Assert.Equal(new DateTime(2030, 7, 1), d.Calendar.SelectedDate);
            d.Calendar.SelectedDate = new DateTime(2030, 7, 4);
            MouseDown(d.Calendar); // pressing in the calendar doesn't drag the dialog
            MouseDown(Named<TextBlock>(d, "TitleText")); // pressing elsewhere drags it (needs the real mouse)
            Click(Named<Button>(d, "SaveButton"));
        });
        Assert.Equal(new DateTime(2030, 7, 4), result);
    }

    [AvaloniaFact]
    public void An_overdue_date_starts_on_today_and_past_days_are_blocked()
    {
        var result = PickDue(new DateTime(2030, 6, 1), d =>
        {
            Assert.Equal(Morning.Date, d.Calendar.SelectedDate);
            Assert.True(d.Calendar.BlackoutDates.Contains(Morning.Date.AddDays(-1)));
            Assert.False(d.Calendar.BlackoutDates.Contains(Morning.Date));
            Cancel(d);
        });
        Assert.Null(result);
    }

    [AvaloniaFact]
    public void Saving_without_a_day_shows_an_error()
    {
        var result = PickDue(null, d =>
        {
            d.Calendar.SelectedDate = null;
            d.ClickSave();
            Assert.Equal("Pick a day.", d.Error.Text);
            Assert.True(d.Error.IsVisible);
            Assert.Equal("Pick a day.", A11y.LastAnnouncement);
            Press(d, Key.Escape);
        });
        Assert.Null(result);
    }

    [AvaloniaFact]
    public void Reminder_quick_choices_in_the_morning()
    {
        var result = PickReminder(null, new DateTime(2030, 6, 20), d =>
        {
            Assert.Equal("Add reminder", d.Title);
            Assert.Equal(new object[] { "In 1 hour", "This evening", "Tomorrow 9:00", "Thu 20 Jun 9:00" }, Quick(d).Select(b => b.Content!));
            Assert.True(Named<Grid>(d, "TimeRow").IsVisible);
            Assert.True(d.Time.IsFocused);
            Assert.Equal(new DateTime(2030, 6, 20), d.Calendar.SelectedDate); // starts on the due date
            Click(Quick(d)[0]);
        });
        Assert.Equal(new DateTime(2030, 6, 12, 11, 15, 0), result); // seconds dropped
    }

    [AvaloniaTheory]
    [InlineData(1, "2030-06-12 18:00")]
    [InlineData(2, "2030-06-13 09:00")]
    [InlineData(3, "2030-06-20 09:00")]
    public void Other_reminder_quick_choices(int button, string expected)
    {
        var result = PickReminder(null, new DateTime(2030, 6, 20), d => Click(Quick(d)[button]));
        Assert.Equal(DateTime.Parse(expected), result);
    }

    [AvaloniaFact]
    public void In_the_evening_there_is_no_this_evening_and_no_chip_for_tomorrow_or_past_due_dates()
    {
        DateDialog.Clock = () => new DateTime(2030, 6, 12, 20, 0, 0);
        PickReminder(null, new DateTime(2030, 6, 13), d =>
        {
            Assert.Equal(new object[] { "In 1 hour", "Tomorrow 9:00" }, Quick(d).Select(b => b.Content!));
            Cancel(d);
        });
        PickReminder(null, new DateTime(2030, 6, 1), d =>
        {
            Assert.Equal(new object[] { "In 1 hour", "Tomorrow 9:00" }, Quick(d).Select(b => b.Content!));
            Cancel(d);
        });
    }

    [AvaloniaFact]
    public void Typing_a_time_and_saving()
    {
        var result = PickReminder(new DateTime(2030, 6, 14, 8, 30, 0), null, d =>
        {
            Assert.Equal("Change reminder", d.Title);
            Assert.Equal("08:30", d.Time.Text);
            d.Time.Text = "17.45";
            Click(Named<Button>(d, "SaveButton"));
        });
        Assert.Equal(new DateTime(2030, 6, 14, 17, 45, 0), result);
    }

    [AvaloniaFact]
    public void Invalid_and_past_times_are_explained()
    {
        var result = PickReminder(null, null, d =>
        {
            d.Time.Text = "25:00";
            Assert.False(d.Error.IsVisible); // not while typing…
            d.ClickSave();
            Assert.Equal("Enter a time like 9:30 or 17:00.", d.Error.Text); // …only on Save
            d.Time.Text = "9:00"; // today 9:00 is before 10:15
            d.ClickSave();
            Assert.Equal("That time has already passed.", d.Error.Text);
            d.Time.Text = "11:00";
            Assert.False(d.Error.IsVisible); // fixed while typing: the error goes away
            Cancel(d);
        });
        Assert.Null(result);
    }

    [AvaloniaFact]
    public void Text_size_scales_the_date_dialog()
    {
        A11y.TextScaleSource = () => 1.25;
        A11y.RefreshTextScale();
        PickDue(null, d =>
        {
            Assert.IsType<ScaleTransform>(Named<LayoutTransformControl>(d, "Scaler").LayoutTransform);
            Cancel(d);
        });
    }
}
