using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PinList;

/// <summary>Picks a due date, or a reminder date and time. Styled like the app's menus.</summary>
public partial class DateDialog : Window
{
    /// <summary>The current time. Tests replace it (the quick choices depend on the time of day).</summary>
    internal static Func<DateTime> Clock { get; set; } = SystemClock;

    internal static DateTime SystemClock() => DateTime.Now;

    readonly bool _withTime;
    DateTime? _result;

    DateDialog(string title, DateTime? initial, bool withTime)
    {
        InitializeComponent();
        _withTime = withTime;
        TitleText.Text = title;
        Title = title; // announced by screen readers when the dialog opens
        A11y.ApplyTextScale(this);

        // an overdue date starts on today, because past days are blocked below
        var day = initial?.Date is { } d && d >= Clock().Date ? d : Clock().Date;
        Cal.SelectedDate = day;
        Cal.DisplayDate = day;
        // past days can't be picked (a due date or reminder in the past makes no sense)
        Cal.BlackoutDates.Add(new CalendarDateRange(DateTime.MinValue, Clock().Date.AddDays(-1)));

        if (withTime)
        {
            TimeRow.Visibility = Visibility.Visible;
            TimeBox.Text = initial?.ToString("HH:mm") ?? "";
        }

        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.Source is not Calendar) DragMove(); };
        SourceInitialized += (_, _) => Native.MakeToolWindow(this);
        // The calendar keeps the mouse captured after a click, which would swallow the next click on Save
        Cal.PreviewMouseUp += (_, _) => { if (Mouse.Captured is CalendarItem) Mouse.Capture(null); };
        Cal.SelectedDatesChanged += (_, _) => Validate();
        Loaded += (_, _) =>
        {
            if (withTime) { TimeBox.Focus(); TimeBox.SelectAll(); }
            else Cal.Focus();
        };
    }

    void AddQuick(string text, Func<DateTime> value)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("ChipButton") };
        b.Click += (_, _) => { _result = value(); DialogResult = true; };
        QuickPanel.Children.Add(b);
    }

    /// <summary>Returns the picked due date, or null if cancelled.</summary>
    public static DateTime? PickDueDate(Window owner, DateTime? current)
    {
        var dlg = new DateDialog(current == null ? "Add due date" : "Change due date", current, withTime: false);
        var today = Clock().Date;
        dlg.AddQuick("Today", () => today);
        dlg.AddQuick("Tomorrow", () => today.AddDays(1));
        dlg.AddQuick("Next week", () => NextMonday(today));
        return dlg.Run(owner);
    }

    /// <summary>Returns the picked reminder time, or null if cancelled.</summary>
    public static DateTime? PickReminder(Window owner, DateTime? current, DateTime? dueDate)
    {
        var dlg = new DateDialog(current == null ? "Add reminder" : "Change reminder", current ?? dueDate, withTime: true);
        var now = Clock();
        dlg.AddQuick("In 1 hour", () => TrimSeconds(Clock().AddHours(1)));
        if (now.Hour < 17) dlg.AddQuick("This evening", () => Clock().Date.AddHours(18));
        dlg.AddQuick("Tomorrow 9:00", () => Clock().Date.AddDays(1).AddHours(9));
        if (dueDate is { } due && due.AddHours(9) > now && due != Clock().Date.AddDays(1))
            dlg.AddQuick($"{DateText.Day(due, Clock().Date)} 9:00", () => due.AddHours(9));
        return dlg.Run(owner);
    }

    static DateTime NextMonday(DateTime d)
    {
        int days = ((int)DayOfWeek.Monday - (int)d.DayOfWeek + 7) % 7;
        return d.AddDays(days == 0 ? 7 : days);
    }

    static DateTime TrimSeconds(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);

    DateTime? Run(Window owner)
    {
        if (owner.IsVisible) Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return ShowDialog() == true ? _result : null;
    }

    void TimeBox_TextChanged(object sender, TextChangedEventArgs e) => Validate(showErrors: false);

    /// <summary>Checks the input; returns the value or null (and shows why when showErrors is true).</summary>
    DateTime? Validate(bool showErrors = false)
    {
        string? error = null;
        DateTime? value = null;

        if (Cal.SelectedDate is not { } day) error = "Pick a day.";
        else if (!_withTime) value = day.Date;
        else if (!DateText.TryParseTime(TimeBox.Text, out var time)) error = "Enter a time like 9:30 or 17:00.";
        else if (day.Date + time <= Clock()) error = "That time has already passed.";
        else value = day.Date + time;

        if (error == null || showErrors)
        {
            ErrorText.Text = error ?? "";
            ErrorText.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            if (error != null && showErrors) A11y.Announce(this, error);
        }
        return value;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var value = Validate(showErrors: true);
        if (value == null) return;
        _result = value;
        DialogResult = true;
    }
}
