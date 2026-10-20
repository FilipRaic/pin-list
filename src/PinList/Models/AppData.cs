using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PinList;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class TodoItem : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    string _text = "";
    public string Text { get => _text; set { if (Set(ref _text, value)) Raise(nameof(AccessibleName)); } }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    DateTime? _dueDate;
    /// <summary>Due day (date only), or null.</summary>
    public DateTime? DueDate
    {
        get => _dueDate;
        set { if (Set(ref _dueDate, value?.Date)) RefreshDue(); }
    }

    DateTime? _reminderAt;
    /// <summary>When to show a reminder notification, or null.</summary>
    public DateTime? ReminderAt
    {
        get => _reminderAt;
        set { if (Set(ref _reminderAt, value)) RefreshReminder(); }
    }

    bool _reminderShown;
    /// <summary>True once the reminder notification was shown (so it is shown only once).</summary>
    public bool ReminderShown
    {
        get => _reminderShown;
        set { if (Set(ref _reminderShown, value)) RefreshReminder(); }
    }

    // ---- For display only ----

    [JsonIgnore] public bool HasDue => DueDate != null;
    [JsonIgnore] public bool IsOverdue => DueDate is { } d && d < DateTime.Today;
    [JsonIgnore] public bool IsDueToday => DueDate is { } d && d == DateTime.Today;
    [JsonIgnore] public string DueText => DueDate is { } d ? DateText.Day(d) : "";

    [JsonIgnore] public bool HasPendingReminder => ReminderAt != null && !ReminderShown;
    [JsonIgnore] public string ReminderText => ReminderAt is { } r ? DateText.DayAndTime(r) : "";

    /// <summary>Called when the date changes (midnight) so "Today" / "Overdue" are updated.</summary>
    public void RefreshDue()
    {
        Raise(nameof(HasDue));
        Raise(nameof(IsOverdue));
        Raise(nameof(IsDueToday));
        Raise(nameof(DueText));
        RefreshReminder();
    }

    void RefreshReminder()
    {
        Raise(nameof(HasPendingReminder));
        Raise(nameof(ReminderText));
        Raise(nameof(AccessibleName));
    }

    /// <summary>What a screen reader says for the item, e.g. "Send the invoice, due today, reminder Tomorrow 09:00".</summary>
    [JsonIgnore]
    public string AccessibleName
    {
        get
        {
            var name = Text.Replace("\n", ", ");
            if (DueDate != null) name += IsOverdue ? $", overdue, was due {DueText}" : $", due {DueText}";
            if (HasPendingReminder) name += $", reminder {ReminderText}";
            return name;
        }
    }

    public override string ToString() => AccessibleName;

    // UI state only, not saved
    bool _isEditing, _isCompleting, _isActive;
    [JsonIgnore] public bool IsEditing { get => _isEditing; set => Set(ref _isEditing, value); }
    [JsonIgnore] public bool IsCompleting { get => _isCompleting; set => Set(ref _isCompleting, value); }
    [JsonIgnore] public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}

public class Category : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    string _name = "New category";
    public string Name { get => _name; set { if (Set(ref _name, value)) Raise(nameof(AccessibleName)); } }

    string _color = "#60CDFF";
    public string Color { get => _color; set => Set(ref _color, value); }

    bool _collapsed;
    public bool Collapsed { get => _collapsed; set { if (Set(ref _collapsed, value)) Raise(nameof(AccessibleName)); } }

    ObservableCollection<TodoItem> _items = new();
    public ObservableCollection<TodoItem> Items
    {
        get => _items;
        set
        {
            _items.CollectionChanged -= OnItemsChanged;
            _items = value ?? new();
            _items.CollectionChanged += OnItemsChanged;
        }
    }

    public Category() => _items.CollectionChanged += OnItemsChanged;

    void OnItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Raise(nameof(AccessibleName));

    /// <summary>What a screen reader says for the category header, e.g. "Work, 5 items, collapsed".</summary>
    [JsonIgnore]
    public string AccessibleName =>
        $"{Name}, {(Items.Count == 1 ? "1 item" : $"{Items.Count} items")}{(Collapsed ? ", collapsed" : "")}";

    public override string ToString() => AccessibleName;

    bool _isEditing, _isActive;
    [JsonIgnore] public bool IsEditing { get => _isEditing; set => Set(ref _isEditing, value); }
    [JsonIgnore] public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}

public class Board : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    string _title = "My list";
    public string Title { get => _title; set { if (Set(ref _title, value)) Raise(nameof(WindowTitle)); } }

    /// <summary>Window title for screen readers and the Windows task switcher.</summary>
    [JsonIgnore]
    public string WindowTitle => string.IsNullOrWhiteSpace(Title) ? "PinList" : $"{Title} – PinList";

    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 480;

    bool _locked;
    public bool Locked { get => _locked; set => Set(ref _locked, value); }

    /// <summary>Key from Themes.BoardStyles (Dark, Light, Yellow, ...).</summary>
    public string Style { get; set; } = "Dark";

    /// <summary>Semi-transparent background (fixed opacity) or solid color.</summary>
    public bool Transparent { get; set; } = true;

    public ObservableCollection<Category> Categories { get; set; } = new();

    bool _isEditingTitle;
    [JsonIgnore] public bool IsEditingTitle { get => _isEditingTitle; set => Set(ref _isEditingTitle, value); }
}

public class ArchivedItem
{
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public DateTime? DueDate { get; set; }
    public string CategoryId { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public string CategoryColor { get; set; } = "#A0A0A0";
    public string BoardId { get; set; } = "";
    public string BoardTitle { get; set; } = "";

    /// <summary>What a screen reader says for the archive row.</summary>
    public override string ToString() =>
        $"{Text.Replace("\n", ", ")}, completed {CompletedAt:g}, category {CategoryName}";
}

public class Settings
{
    /// <summary>Theme for menus, dialogs and the archive: "System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
}

public class AppData
{
    /// <summary>Version of the data format. Store.Migrate upgrades older files.</summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public Settings Settings { get; set; } = new();
    public ObservableCollection<Board> Boards { get; set; } = new();
    public ObservableCollection<ArchivedItem> Archive { get; set; } = new();
}

/// <summary>Short, friendly date texts for due dates and reminders.</summary>
public static class DateText
{
    static CultureInfo C => CultureInfo.CurrentCulture;

    /// <summary>"Today", "Tomorrow", "Yesterday", "Mon 26 Oct", or "26 Oct 2027" for another year.</summary>
    public static string Day(DateTime d, DateTime? today = null)
    {
        var t = (today ?? DateTime.Today).Date;
        d = d.Date;
        if (d == t) return "Today";
        if (d == t.AddDays(1)) return "Tomorrow";
        if (d == t.AddDays(-1)) return "Yesterday";
        return d.Year == t.Year ? d.ToString("ddd d MMM", C) : d.ToString("d MMM yyyy", C);
    }

    public static string DayAndTime(DateTime d, DateTime? today = null) =>
        $"{Day(d, today)} {d.ToString("HH:mm", C)}";

    /// <summary>Accepts "9", "930", "0930", "9:30", "9.30" and "21:05". Returns false for anything else.</summary>
    public static bool TryParseTime(string? s, out TimeSpan time)
    {
        time = default;
        s = (s ?? "").Trim().Replace('.', ':');
        if (s.Length == 0) return false;

        int h, m = 0;
        if (s.Contains(':'))
        {
            var parts = s.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out h) || !int.TryParse(parts[1], out m) || parts[1].Length != 2)
                return false;
        }
        else
        {
            if (!int.TryParse(s, out int n) || s.Length > 4) return false;
            if (s.Length <= 2) h = n;
            else { h = n / 100; m = n % 100; }
        }
        if (h is < 0 or > 23 || m is < 0 or > 59) return false;
        time = new TimeSpan(h, m, 0);
        return true;
    }
}
