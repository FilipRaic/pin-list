using System;
using Avalonia.Input;

namespace PinList;

/// <summary>
/// Words and keyboard shortcuts that differ between Windows and macOS. A Mac shows shortcuts with its own key
/// symbols (⌘Z, ↩, ⌫, ⌥↑) and has Cmd where Windows has Ctrl. Tests switch <see cref="IsMac"/> to check both.
/// </summary>
static class OsText
{
    /// <summary>True on a Mac. Tests set it to see the Mac words and keys on Windows.</summary>
    public static bool IsMac { get; set; } = OperatingSystem.IsMacOS();

    /// <summary>"Exit" on Windows, "Quit PinList" on a Mac.</summary>
    public static string Exit => IsMac ? "Quit PinList" : "Exit";

    /// <summary>The menu entry that starts PinList with the computer.</summary>
    public static string Autostart => OperatingSystem.IsWindows() && !IsMac ? "Start with Windows" : "Open at login";

    // ================= Shortcuts shown to the user =================

    /// <summary>The command key with a letter: "Ctrl+Z" on Windows, "⌘Z" on a Mac.</summary>
    public static string Command(string key) => IsMac ? "⌘" + key : "Ctrl+" + key;

    /// <summary>Alt (Option on a Mac) with a key: "Alt+Up" on Windows, "⌥↑" on a Mac.</summary>
    public static string Alt(string key) => IsMac ? "⌥" + Symbol(key) : "Alt+" + key;

    /// <summary>The key that confirms: "Enter" on Windows, "↩" (Return) on a Mac.</summary>
    public static string Enter => IsMac ? "↩" : "Enter";

    /// <summary>The key that deletes: "Del" on Windows, "⌫" (the delete key) on a Mac.</summary>
    public static string Delete => IsMac ? "⌫" : "Del";

    /// <summary>The same keys in words, for screen readers and sentences.</summary>
    public static string EnterName => IsMac ? "Return" : "Enter";
    public static string DeleteName => "Delete";
    public static string ShiftEnterName => IsMac ? "Shift+Return" : "Shift+Enter";

    public static string Undo => Command("Z");
    public static string CopyGesture => Command("C");
    public static string Find => Command("F");

    static string Symbol(string key) => key switch { "Up" => "↑", "Down" => "↓", "Left" => "←", "Right" => "→", _ => key };

    // ================= Help texts with shortcuts =================

    public static string CategoryHelp =>
        $"{EnterName} collapses or expands, F2 renames, {DeleteName} deletes, {AltName}Up or Down moves, context menu key for more";

    public static string ItemHelp =>
        $"Space completes, {EnterName} edits, {DeleteName} deletes, {AltName}Up or Down moves, context menu key for more";

    public static string AddItemTip => $"{EnterName} adds the item · {ShiftEnterName} starts a new line";

    static string AltName => IsMac ? "Option+" : "Alt+";

    // ================= Keys pressed =================

    /// <summary>Ctrl, or Cmd on a Mac (Ctrl keeps working there too).</summary>
    public static bool IsCommand(KeyModifiers mods) => mods == KeyModifiers.Control || (IsMac && mods == KeyModifiers.Meta);

    /// <summary>The delete key: Delete on Windows. A Mac keyboard's delete key is Backspace, so both work there.</summary>
    public static bool IsDeleteKey(Key key) => key == Key.Delete || (IsMac && key == Key.Back);
}
