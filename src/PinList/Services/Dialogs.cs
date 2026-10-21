using System;
using System.Windows;
using Microsoft.Win32;

namespace PinList;

/// <summary>Every dialog the app shows. Tests replace <see cref="Dialogs.Current"/> to answer them.</summary>
public interface IDialogs
{
    /// <summary>Returns the index of the clicked button, or -1 for Esc.</summary>
    int Choose(Window? owner, string title, string message, int defaultIndex, params (string text, DialogButtonKind kind)[] buttons);

    DateTime? PickDueDate(Window owner, DateTime? current);
    DateTime? PickReminder(Window owner, DateTime? current, DateTime? dueDate);

    /// <summary>Returns the chosen path, or null if cancelled.</summary>
    string? SaveFile(Window? owner, string title, string fileName, string filter);

    /// <summary>Returns the chosen path, or null if cancelled.</summary>
    string? OpenFile(Window? owner, string title, string filter);
}

/// <summary>The real dialogs.</summary>
public sealed class WindowsDialogs : IDialogs
{
    public int Choose(Window? owner, string title, string message, int defaultIndex, params (string text, DialogButtonKind kind)[] buttons) =>
        ConfirmDialog.Choose(owner, title, message, defaultIndex, buttons);

    public DateTime? PickDueDate(Window owner, DateTime? current) => DateDialog.PickDueDate(owner, current);

    public DateTime? PickReminder(Window owner, DateTime? current, DateTime? dueDate) => DateDialog.PickReminder(owner, current, dueDate);

    public string? SaveFile(Window? owner, string title, string fileName, string filter)
    {
        var dlg = new SaveFileDialog { Title = title, FileName = fileName, Filter = filter, AddExtension = true, DefaultExt = ".json" };
        return dlg.ShowDialog(owner) == true ? dlg.FileName : null;
    }

    public string? OpenFile(Window? owner, string title, string filter)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter };
        return dlg.ShowDialog(owner) == true ? dlg.FileName : null;
    }
}

public static class Dialogs
{
    public static IDialogs Current { get; set; } = new WindowsDialogs();

    /// <summary>Cancel / dangerous action (e.g. "Delete"). Returns true if confirmed.</summary>
    public static bool Ask(Window? owner, string title, string message, string okText) =>
        Current.Choose(owner, title, message, 1, ("Cancel", DialogButtonKind.Normal), (okText, DialogButtonKind.Danger)) == 1;

    /// <summary>A message with an OK button.</summary>
    public static void Info(Window? owner, string title, string message) =>
        Current.Choose(owner, title, message, 0, ("OK", DialogButtonKind.Accent));
}
