using Avalonia.Controls;

namespace PinList.Tests;

/// <summary>Tray icon that records notifications and lets tests "click" it.</summary>
public sealed class FakeTray : ITrayIcon
{
    public readonly List<(string title, string text)> Notifications = new();
    public IReadOnlyList<TrayEntry> Menu = Array.Empty<TrayEntry>();
    public bool Disposed;

    public event Action? LeftClick;
    public event Action? RightClick;
    public event Action? NotificationClicked;

    public void ShowNotification(string title, string text) => Notifications.Add((title, text));
    public void SetMenu(IReadOnlyList<TrayEntry> entries) => Menu = entries;
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

    public Task<int> Choose(Window? owner, string title, string message, int defaultIndex, params (string text, DialogButtonKind kind)[] buttons)
    {
        Shown.Add($"{title}: {message}");
        return Task.FromResult(ChooseAnswer(title));
    }

    public Task<DateTime?> PickDueDate(Window owner, DateTime? current)
    {
        Shown.Add("due date");
        return Task.FromResult(DueDateAnswer);
    }

    public Task<DateTime?> PickReminder(Window owner, DateTime? current, DateTime? dueDate)
    {
        Shown.Add("reminder");
        return Task.FromResult(ReminderAnswer);
    }

    public Task<string?> SaveFile(Window? owner, string title, string fileName)
    {
        Shown.Add("save: " + fileName);
        return Task.FromResult(SaveFileAnswer);
    }

    public Task<string?> OpenFile(Window? owner, string title)
    {
        Shown.Add("open");
        return Task.FromResult(OpenFileAnswer);
    }

    /// <summary>The user finishes in the browser right away (what the sign-in itself returns decides).</summary>
    public Task<bool> WaitForSignIn(Window? owner, Func<CancellationToken, Task<bool>> signIn)
    {
        Shown.Add("waiting for sign-in");
        return signIn(CancellationToken.None);
    }
}
