using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;

namespace PinList;

public enum WidgetRowKind { Category, Item }

/// <summary>
/// One row of a home screen widget: a category header or an item. Colors are ARGB (0xAARRGGBB), the form Android
/// takes. <paramref name="Id"/> is the category's or the item's id, <paramref name="Color"/> the category's name
/// color or the item's ring color.
/// </summary>
public sealed record WidgetRow(
    WidgetRowKind Kind,
    string Id,
    string Text,
    uint Color,
    int Count = 0,
    bool Collapsed = false,
    uint PillColor = 0,
    string Due = "",
    uint DueColor = 0,
    uint DueBackground = 0,
    bool HasReminder = false,
    string AccessibleName = "");

/// <summary>The colors of a home screen widget, from the list's own color (ARGB).</summary>
public sealed record WidgetLook(uint Background, uint Text, uint Muted, uint Accent, uint Line, bool IsDark);

public enum WidgetStatus { Ok, Empty, Deleted }

/// <summary>Everything a home screen widget shows for one list.</summary>
public sealed record WidgetState(
    WidgetStatus Status,
    string BoardId,
    string Title,
    WidgetLook Look,
    IReadOnlyList<WidgetRow> Rows,
    bool CanSync,
    bool HasCategories);

/// <summary>
/// The colors of the small screens a widget opens over the home screen (the add box, the list chooser): the app's
/// own sheet colors, in the app's theme (ARGB).
/// </summary>
public sealed record WidgetChrome(uint Background, uint Field, uint Text, uint Muted, uint Accent, uint AccentText, bool IsDark);

/// <summary>A list to choose when a home screen widget is placed.</summary>
public sealed record WidgetChoice(string BoardId, string Title, uint Swatch, int OpenCount)
{
    public string CountText => OpenCount == 1 ? "1 item" : $"{OpenCount} items";
}

/// <summary>
/// What an Android home screen widget shows. The Android project only draws this, so everything that can be
/// decided without Android is decided (and tested) here.
/// </summary>
public static class WidgetModel
{
    public const string DeletedText = "This list was deleted";
    public const string ChooseText = "Choose a list";
    public const string EmptyText = "No items yet";
    public const string NoListsText = "No lists yet. Open PinList to create one.";
    public const string CompletedText = "Completed";
    public const string UndoText = "Undo";

    // What the widget's sync button says (short messages over the home screen)
    public const string SyncingText = "Syncing";
    public const string SyncedText = "Synced";
    public const string OfflineText = "Offline. Changes sync later.";
    public const string SyncWaitingText = "Can't reach Google Drive. PinList keeps trying.";
    public const string SyncProblemText = "Sync problem. Open PinList to fix it.";

    /// <summary>
    /// The widget of the list with this id. A list that is gone gives <see cref="WidgetStatus.Deleted"/>, a list
    /// without items <see cref="WidgetStatus.Empty"/> (its category headers are still rows).
    /// </summary>
    public static WidgetState For(AppData data, string? boardId, bool signedIn)
    {
        var board = data.Boards.FirstOrDefault(b => b.Id == boardId);
        if (board == null)
            return new WidgetState(WidgetStatus.Deleted, boardId ?? "", "", Look(Themes.Get("Dark"), false, out _),
                Array.Empty<WidgetRow>(), false, false);

        var look = Look(Themes.Get(board.Style), board.Transparent, out var colors);
        var rows = new List<WidgetRow>();
        bool anyItem = false;
        foreach (var cat in board.Categories)
        {
            rows.Add(new WidgetRow(WidgetRowKind.Category, cat.Id, cat.Name,
                Argb(Themes.CategoryColor(cat.Color, board.Style, CategoryPart.Text)),
                Count: cat.Items.Count,
                Collapsed: cat.Collapsed,
                PillColor: Argb(Themes.CategoryColor(cat.Color, board.Style, CategoryPart.Pill)),
                AccessibleName: cat.AccessibleName));
            anyItem |= cat.Items.Count > 0;
            if (cat.Collapsed) continue;

            uint ring = Argb(Themes.CategoryColor(cat.Color, board.Style, CategoryPart.Ring));
            foreach (var item in cat.Items)
            {
                string chip = item.IsOverdue ? "Overdue" : item.IsDueToday ? "Accent" : "";
                rows.Add(new WidgetRow(WidgetRowKind.Item, item.Id, PlainBreaks(item.Text), ring,
                    Due: item.DueText,
                    DueColor: item.HasDue ? colors(chip.Length > 0 ? chip : "TextSecondary") : 0,
                    DueBackground: item.HasDue ? colors(chip.Length > 0 ? chip + "Bg" : "ChipBg") : 0,
                    HasReminder: item.HasPendingReminder,
                    AccessibleName: item.AccessibleName));
            }
        }

        return new WidgetState(anyItem ? WidgetStatus.Ok : WidgetStatus.Empty, board.Id, board.Title, look, rows,
            signedIn && board.Synced, board.Categories.Count > 0);
    }

    /// <summary>Every list, in the app's order, for the screen that asks which list a new widget shows.</summary>
    public static IReadOnlyList<WidgetChoice> Choices(AppData data) =>
        data.Boards.Select(b => new WidgetChoice(
            b.Id,
            string.IsNullOrWhiteSpace(b.Title) ? "No title" : b.Title,
            Argb(Themes.Parse(Themes.Get(b.Style).Background)),
            b.Categories.Sum(c => c.Items.Count))).ToList();

    /// <summary>
    /// The colors of the add box and the list chooser. They follow the app's theme setting ("Light", "Dark", or the
    /// phone's own setting for anything else), like the app's menus and sheets.
    /// </summary>
    public static WidgetChrome Chrome(string? themeSetting, bool systemDark)
    {
        bool dark = themeSetting switch
        {
            "Light" => false,
            "Dark" => true,
            _ => systemDark,
        };
        var r = new ResourceDictionary();
        Themes.ApplyChrome(r, dark);
        uint Get(string key) => Argb(((ISolidColorBrush)r[key]!).Color);
        return new WidgetChrome(Get("Raised"), Get("Hover"), Get("Text1"), Get("Text3"), Get("Accent"), Get("AccentText"), dark);
    }

    /// <summary>The title of the box that adds items from a widget: "Add to Work", or "Add item" without a list title.</summary>
    public static string AddTitle(string boardTitle) =>
        string.IsNullOrWhiteSpace(boardTitle) ? "Add item" : $"Add to {boardTitle}";

    /// <summary>What a screen reader says for an item's circle: "Complete Send the invoice" (the first line).</summary>
    public static string CompleteName(string itemText) => $"Complete {PlainBreaks(itemText).Split('\n')[0]}";

    static string PlainBreaks(string text) => text.Replace("\r\n", "\n");

    static uint Argb(Color c) => c.ToUInt32();

    /// <summary>The list's colors, the same ones a desktop widget gets (<see cref="Themes.ApplyBoard"/>).</summary>
    static WidgetLook Look(BoardStyle style, bool transparent, out Func<string, uint> colors)
    {
        var r = new ResourceDictionary();
        Themes.ApplyBoard(r, style, transparent);
        Func<string, uint> get = key => Argb(((ISolidColorBrush)r[key]!).Color);
        colors = get;
        return new WidgetLook(get("BoardBg"), get("TextPrimary"), get("TextMuted"), get("Accent"), get("Line"), style.IsDark);
    }
}
