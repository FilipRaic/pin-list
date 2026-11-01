using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Xunit;

namespace PinList.Tests;

/// <summary>
/// Base for tests that use real windows of the running test app (see <see cref="UiHost"/>).
/// Each test starts with no widgets and empty data in its own folder. Mark test methods with [AvaloniaFact],
/// so they run on the app's UI thread.
/// </summary>
public abstract class UiTest : IDisposable
{
    protected readonly string Dir = Path.Combine(Path.GetTempPath(), "PinListTests", Guid.NewGuid().ToString("N"));
    protected readonly List<(string message, bool isError)> Messages = new();
    internal readonly ManualSaver Saver = new();

    protected static App App => App.Instance;
    protected static FakeDialogs Dialogs => UiHost.Dialogs;
    protected static FakeTray Tray => UiHost.Tray;

    protected UiTest()
    {
        UiHost.EnsureStarted();
        while (UiHost.UnhandledErrors.TryDequeue(out _)) { }
        ResetApp();
        Store.UseDirectory(Dir);
        Store.Saver = Saver;
        Store.Notify = (m, e) => Messages.Add((m, e));
    }

    public virtual void Dispose()
    {
        Pump();
        ResetApp();
        try { Directory.Delete(Dir, true); } catch { }
        var errors = UiHost.UnhandledErrors.ToArray();
        while (UiHost.UnhandledErrors.TryDequeue(out _)) { }
        Assert.Empty(errors);
    }

    static void ResetApp()
    {
        var app = App.Instance;
        BoardWindow.EndDrag(); // a test that failed in the middle of a drag must not leave it going
        foreach (var w in app.OpenWindows.ToList())
        {
            w.LastMenu?.Close();
            w.CloseForReload();
        }
        app.LastTrayMenu?.Close();
        app.Archive?.Close();
        DialogHost.Current?.Close();
        Pump();
        App.ShuttingDown = false;
        app.ReminderService.Stop();
        app.ReminderService.Clock = () => DateTime.Now;
        Actions.ClearUndo();
        BoardWindow.SetActive(null);
        BoardWindow.CompleteDelay = TimeSpan.FromMilliseconds(900);
        BoardWindow.SystemResize = true;
        Themes.ContrastSource = () => null;
        Themes.SystemDarkSource = () => true;
        A11y.TextScaleSource = () => 1.0;
        A11y.RefreshTextScale();
        A11y.LastAnnouncement = null;
        DateDialog.Clock = DateDialog.SystemClock;
        Dialogs.Reset();
        Tray.Notifications.Clear();
        UiHost.OpenedFolders.Clear();
        UiHost.ShutdownRequests = 0;
        Store.Data.Settings.Theme = "Dark";
        app.ApplyTheme();
        app.UseSync(UiHost.UnavailableSync());
    }

    protected static void Pump() => UiHost.Pump();

    // ================= Widgets =================

    /// <summary>A board with categories and items, added to the data.</summary>
    protected static Board MakeBoard(params (string name, string[] items)[] categories)
    {
        var b = new Board { Title = "Test", X = 100, Y = 100, Width = 320, Height = 480, Style = "Dark", Transparent = false };
        foreach (var (name, items) in categories)
        {
            var c = new Category { Name = name };
            foreach (var t in items) c.Items.Add(new TodoItem { Text = t });
            b.Categories.Add(c);
        }
        Store.Data.Boards.Add(b);
        return b;
    }

    /// <summary>Opens the board's widget through the app (like on start) and waits until it's laid out.</summary>
    protected static BoardWindow Open(Board board)
    {
        if (!Store.Data.Boards.Contains(board)) Store.Data.Boards.Add(board);
        App.OpenBoard(board);
        var w = App.OpenWindows.Last();
        w.Activate();
        Pump();
        return w;
    }

    protected static BoardWindow OpenNew(params (string name, string[] items)[] categories) => Open(MakeBoard(categories));

    /// <summary>The focusable row of an item or category.</summary>
    protected static AccessibleRow Row(Visual w, object tag) =>
        Find<AccessibleRow>(w, r => ReferenceEquals(r.Tag, tag)) ?? throw new InvalidOperationException("No row for " + tag);

    protected static T? Find<T>(Visual root, Func<T, bool>? match = null) where T : class =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(x => match == null || match(x));

    protected static IEnumerable<T> FindAll<T>(Visual root) => root.GetVisualDescendants().OfType<T>();

    protected static T Named<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name) ?? throw new InvalidOperationException("No element " + name);

    protected static TextBox AddBox(Visual w, Category cat) =>
        Find<TextBox>(w, t => t.Classes.Contains("addbox") && ReferenceEquals(t.Tag, cat))!;

    /// <summary>The visible edit box (item text, category name or title) for a data object.</summary>
    protected static TextBox EditBox(Visual w, object dataContext) =>
        Find<TextBox>(w, t => t.Classes.Contains("edit") && t.IsEffectivelyVisible && ReferenceEquals(t.DataContext, dataContext))
        ?? throw new InvalidOperationException("No visible edit box");

    /// <summary>The element that has keyboard focus in a window.</summary>
    protected static IInputElement? Focused(TopLevel w) => w.FocusManager?.GetFocusedElement();

    protected static string? NameOf(Control c) => AutomationProperties.GetName(c);

    // ================= Simulated input =================

    /// <summary>Presses a key on an element like the keyboard does (tunnelling through the window, then bubbling).</summary>
    protected static void Press(Interactive target, Key key, KeyModifiers mods = KeyModifiers.None)
    {
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods, Source = target });
        Pump();
    }

    /// <summary>Presses a key on whatever has keyboard focus in the window (the window itself when nothing has).</summary>
    protected static void Press(TopLevel w, Key key, KeyModifiers mods = KeyModifiers.None) =>
        Press((Interactive?)Focused(w) ?? w, key, mods);

    /// <summary>Clicks a button through its real click handling (the Click event and its command).</summary>
    protected static void Click(Button button)
    {
        typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(button, null);
        Pump();
    }

    static readonly Avalonia.Input.Pointer Mouse = new(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);

    /// <summary>Left mouse button down in the middle of an element, with 1 or 2 clicks.</summary>
    protected static void MouseDown(Control target, int clicks = 1)
    {
        var root = (Visual)TopLevel.GetTopLevel(target)!;
        var pos = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), root) ?? default;
        var props = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        target.RaiseEvent(new PointerPressedEventArgs(target, Mouse, root, pos, 0, props, KeyModifiers.None, clicks));
        Pump();
    }

    /// <summary>Right click (or the context menu key): asks the element for its menu.</summary>
    protected static void RightClick(Control target)
    {
        target.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = target });
        Pump();
    }

    /// <summary>
    /// The window loses focus to another app or window. (The headless platform keeps every window active, so the
    /// platform's "deactivated" notification is sent directly.)
    /// </summary>
    protected static void Deactivate(Window w)
    {
        var impl = typeof(TopLevel).GetProperty("PlatformImpl", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(w)!;
        var deactivated = (Action?)impl.GetType().GetProperty("Deactivated")!.GetValue(impl);
        deactivated?.Invoke();
        Pump();
    }

    // ================= Menus =================

    protected static MenuItem MenuItem(ItemsControl menu, params string[] path)
    {
        ItemsControl current = menu;
        MenuItem? found = null;
        foreach (var header in path)
        {
            found = current.Items.OfType<MenuItem>().FirstOrDefault(m => Header(m) == header)
                    ?? throw new InvalidOperationException($"No menu item '{header}' in: " +
                                                           string.Join(", ", current.Items.OfType<MenuItem>().Select(Header)));
            current = found;
        }
        return found!;
    }

    /// <summary>The text of a menu item (items with a shortcut have a panel as header, and the text as their name).</summary>
    protected static string? Header(MenuItem m) => m.Header as string ?? AutomationProperties.GetName(m);

    /// <summary>Clicks a menu item (path: "Window color", "Yellow") and lets its deferred action run.</summary>
    protected static void ChooseFromMenu(ContextMenu menu, params string[] path)
    {
        var item = MenuItem(menu, path);
        menu.Close();
        item.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent, item));
        Pump();
    }
}
