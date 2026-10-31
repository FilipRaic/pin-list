using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PinList;

public enum DialogButtonKind { Normal, Accent, Danger }

/// <summary>Every dialog the app shows. Tests replace <see cref="Dialogs.Current"/> to answer them.</summary>
public interface IDialogs
{
    /// <summary>Returns the index of the clicked button, or -1 for Esc.</summary>
    Task<int> Choose(Window? owner, string title, string message, int defaultIndex, params (string text, DialogButtonKind kind)[] buttons);

    Task<DateTime?> PickDueDate(Window owner, DateTime? current);
    Task<DateTime?> PickReminder(Window owner, DateTime? current, DateTime? dueDate);

    /// <summary>Returns the chosen path, or null if cancelled.</summary>
    Task<string?> SaveFile(Window? owner, string title, string fileName);

    /// <summary>Returns the chosen path, or null if cancelled.</summary>
    Task<string?> OpenFile(Window? owner, string title);
}

/// <summary>The real dialogs.</summary>
public sealed class AppDialogs : IDialogs
{
    public Task<int> Choose(Window? owner, string title, string message, int defaultIndex, params (string text, DialogButtonKind kind)[] buttons) =>
        ConfirmDialog.Choose(owner, title, message, defaultIndex, buttons);

    public Task<DateTime?> PickDueDate(Window owner, DateTime? current) => DateDialog.PickDueDate(owner, current);

    public Task<DateTime?> PickReminder(Window owner, DateTime? current, DateTime? dueDate) => DateDialog.PickReminder(owner, current, dueDate);

    static readonly FilePickerFileType Json = new("PinList data") { Patterns = new[] { "*.json" } };

    public async Task<string?> SaveFile(Window? owner, string title, string fileName)
    {
        var file = await StorageOf(owner).SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = fileName,
            DefaultExtension = "json",
            FileTypeChoices = new[] { Json },
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> OpenFile(Window? owner, string title)
    {
        var files = await StorageOf(owner).OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            FileTypeFilter = new[] { Json, FilePickerFileTypes.All },
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>File pickers need a window; without an owner any open PinList window will do.</summary>
    static IStorageProvider StorageOf(Window? owner) =>
        (owner ?? App.Instance.AnyWindow()).StorageProvider;
}

public static class Dialogs
{
    public static IDialogs Current { get; set; } = new AppDialogs();

    /// <summary>Cancel / dangerous action (e.g. "Delete"). Returns true if confirmed.</summary>
    public static async Task<bool> Ask(Window? owner, string title, string message, string okText) =>
        await Current.Choose(owner, title, message, 1, ("Cancel", DialogButtonKind.Normal), (okText, DialogButtonKind.Danger)) == 1;

    /// <summary>A message with an OK button.</summary>
    public static Task Info(Window? owner, string title, string message) =>
        Current.Choose(owner, title, message, 0, ("OK", DialogButtonKind.Accent));
}
