using System.Windows;

namespace PinList.Tests;

/// <summary>Tray icon that records notifications and lets tests "click" it.</summary>
public sealed class FakeTray : ITrayIcon
{
    public readonly List<(string title, string text)> Notifications = new();
    public bool Disposed;

    public event Action? LeftClick;
    public event Action? RightClick;
    public event Action? NotificationClicked;

    public void ShowNotification(string title, string text) => Notifications.Add((title, text));
    public void Dispose() => Disposed = true;

    public void ClickLeft() => LeftClick?.Invoke();
    public void ClickRight() => RightClick?.Invoke();
    public void ClickNotification() => NotificationClicked?.Invoke();
}

/// <summary>Dialogs answered by the test. Every dialog that was shown is recorded.</summary>
public sealed class FakeDialogs : IDialogs
{
    public readonly List<string> Shown = new();

    /// <summary>Answer for Choose (gets the title; returns the button index). Default: Esc.</summary>
    public Func<string, int> ChooseAnswer = _ => -1;
    public DateTime? DueDateAnswer;
    public DateTime? ReminderAnswer;
    public string? SaveFileAnswer;
    public string? OpenFileAnswer;

    public void Reset()
    {
        Shown.Clear();
        ChooseAnswer = _ => -1;
        DueDateAnswer = ReminderAnswer = null;
        SaveFileAnswer = OpenFileAnswer = null;
    }

    public int Choose(Window? owner, string title, string message, int defaultIndex, params (string text, DialogButtonKind kind)[] buttons)
    {
        Shown.Add($"{title}: {message}");
        return ChooseAnswer(title);
    }

    public DateTime? PickDueDate(Window owner, DateTime? current)
    {
        Shown.Add("due date");
        return DueDateAnswer;
    }

    public DateTime? PickReminder(Window owner, DateTime? current, DateTime? dueDate)
    {
        Shown.Add("reminder");
        return ReminderAnswer;
    }

    public string? SaveFile(Window? owner, string title, string fileName, string filter)
    {
        Shown.Add("save: " + fileName);
        return SaveFileAnswer;
    }

    public string? OpenFile(Window? owner, string title, string filter)
    {
        Shown.Add("open");
        return OpenFileAnswer;
    }
}
