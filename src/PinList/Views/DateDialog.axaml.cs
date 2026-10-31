using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace PinList;

/// <summary>Picks a due date, or a reminder date and time. Styled like the app's menus.</summary>
public partial class DateDialog : Window
{
    /// <summary>The current time. Tests replace it (the quick choices depend on the time of day).</summary>
    internal static Func<DateTime> Clock { get; set; } = SystemClock;

    internal static DateTime SystemClock() => DateTime.Now;

    readonly bool _withTime;
    DateTime? _result;

    /// <summary>For the XAML previewer only.</summary>
    public DateDialog() : this("", null, false) { }

    DateDialog(string title, DateTime? initial, bool withTime)
    {
        InitializeComponent();
        Look.GrayscaleText(this);
        _withTime = withTime;
        TitleText.Text = title;
        Title = title; // announced by screen readers when the dialog opens
        if (A11y.TextScale != 1.0) Scaler.LayoutTransform = new ScaleTransform(A11y.TextScale, A11y.TextScale);

        // an overdue date starts on today, because past days are blocked below
        var today = Clock().Date;
        var day = initial?.Date is { } d && d >= today ? d : today;
        Cal.SelectedDate = day;
        Cal.DisplayDate = day;
        // past days can't be picked (a due date or reminder in the past makes no sense)
        Cal.BlackoutDates.Add(new CalendarDateRange(DateTime.MinValue, today.AddDays(-1)));

        if (withTime)
        {
            TimeRow.IsVisible = true;
            TimeBox.Text = initial?.ToString("HH:mm") ?? "";
        }

        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && (e.Source as Visual)?.FindAncestorOfType<Calendar>(true) == null)
                BeginMoveDrag(e);
        };
        Opened += (_, _) => Native.MakeToolWindow(this);
        Cal.SelectedDatesChanged += (_, _) => Validate();
        TimeBox.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Validate(showErrors: false); };
        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => Close();
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { _result = null; Close(); e.Handled = true; } }, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            if (withTime) { TimeBox.Focus(NavigationMethod.Tab); TimeBox.SelectAll(); }
            else Cal.Focus(NavigationMethod.Tab);
        };
    }

    // ---- For tests ----
    internal Calendar Calendar => Cal;
    internal TextBox Time => TimeBox;
    internal TextBlock Error => ErrorText;
    internal Panel Quick => QuickPanel;
    internal void ClickSave() => Save();

    void AddQuick(string text, Func<DateTime> value)
    {
        var b = new Button { Content = text };
        b.Classes.Add("dialog");
        b.Classes.Add("chip");
        b.Click += (_, _) => { _result = value(); Close(); };
        QuickPanel.Children.Add(b);
    }

    /// <summary>Returns the picked due date, or null if cancelled.</summary>
    public static Task<DateTime?> PickDueDate(Window owner, DateTime? current)
    {
        var dlg = new DateDialog(current == null ? "Add due date" : "Change due date", current, withTime: false);
        var today = Clock().Date;
        dlg.AddQuick("Today", () => today);
        dlg.AddQuick("Tomorrow", () => today.AddDays(1));
        dlg.AddQuick("Next week", () => NextMonday(today));
        return dlg.Run(owner);
    }

    /// <summary>Returns the picked reminder time, or null if cancelled.</summary>
    public static Task<DateTime?> PickReminder(Window owner, DateTime? current, DateTime? dueDate)
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

    async Task<DateTime?> Run(Window owner)
    {
        await DialogHost.Run(this, owner);
        return _result;
    }

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
            ErrorText.IsVisible = error != null;
            if (error != null && showErrors) A11y.Announce(this, error);
        }
        return value;
    }

    void Save()
    {
        var value = Validate(showErrors: true);
        if (value == null) return;
        _result = value;
        Close();
    }
}
