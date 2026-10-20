using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PinList;
using Xunit;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using TextBox = System.Windows.Controls.TextBox;

namespace PinList.Tests;

    /// <summary>
/// Base for tests that use real windows of the running test app (see <see cref="UiHost"/>).
/// Each test starts with no widgets, empty data in its own folder, and real input replaced by simulated input.
/// Test bodies run on the app's thread: wrap them in <c>Ui(() => …)</c>.
/// </summary>
public abstract class UiTest : IDisposable
{
    protected readonly string Dir = Path.Combine(Path.GetTempPath(), "PinListTests", Guid.NewGuid().ToString("N"));
    protected readonly List<(string message, bool isError)> Messages = new();
    internal readonly ManualSaver Saver = new();

    /// <summary>Modifier keys the app sees as held.</summary>
    protected ModifierKeys Held = ModifierKeys.None;

    protected static App App => PinList.App.Instance;
    protected static FakeDialogs Dialogs => UiHost.Dialogs;
    protected static FakeTray Tray => UiHost.Tray;

    protected UiTest()
    {
        while (UiHost.UnhandledErrors.TryDequeue(out _)) { }
        UiHost.Run(() =>
        {
            ResetApp();
            Store.UseDirectory(Dir);
            Store.Saver = Saver;
            Store.Notify = (m, e) => Messages.Add((m, e));
            InputState.Modifiers = () => Held;
        });
    }

    public virtual void Dispose()
    {
        UiHost.Run(() =>
        {
            UiHost.Pump();
            ResetApp();
        });
        try { Directory.Delete(Dir, true); } catch { }
        var errors = UiHost.UnhandledErrors.ToArray();
        while (UiHost.UnhandledErrors.TryDequeue(out _)) { }
        Assert.Empty(errors);
        Assert.Empty(PendingDialogs); // every dialog the test expected did open
    }

    static void ResetApp()
    {
        var app = PinList.App.Instance;
        foreach (var menu in OpenMenus()) menu.IsOpen = false;
        foreach (var w in app.Windows.OfType<Window>().ToList())
        {
            if (w is BoardWindow b) b.CloseForReload();
            else if (w.Left > -30000) w.Close(); // keep the tray menu's invisible host window
        }
        UiHost.Pump();
        PinList.App.ShuttingDown = false;
        app.ReminderService.Stop();
        app.ReminderService.Clock = () => DateTime.Now;
        Actions.ClearUndo();
        InputState.Reset();
        Themes.ForceHighContrast = null;
        Themes.SystemDarkSource = () => true;
        A11y.TextScaleSource = () => 1.0;
        A11y.RefreshTextScale();
        DateDialog.Clock = DateDialog.SystemClock;
        UiHost.Dialogs.Reset();
        UiHost.Tray.Notifications.Clear();
        UiHost.OpenedFolders.Clear();
        UiHost.ShutdownRequests = 0;
        Store.Data.Settings.Theme = "Dark";
        app.ApplyTheme();
    }

    protected static void Ui(Action action) => UiHost.Run(action);
    protected static T Ui<T>(Func<T> func) => UiHost.Run(func);
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
    protected static AccessibleRow Row(Window w, object tag) =>
        Find<AccessibleRow>(w, r => ReferenceEquals(r.Tag, tag)) ?? throw new InvalidOperationException("No row for " + tag);

    protected static T? Find<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject =>
        FindAll<T>(root).FirstOrDefault(x => match == null || match(x));

    protected static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in FindAll<T>(child)) yield return deeper;
        }
    }

    protected static T Named<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new InvalidOperationException("No element " + name);

    protected static TextBox AddBox(Window w, Category cat) =>
        Find<TextBox>(w, t => t.Name == "AddBox" && ReferenceEquals(t.Tag, cat))!;

    /// <summary>The visible edit box (item text, category name or title) for a data object.</summary>
    protected static TextBox EditBox(Window w, object dataContext) =>
        Find<TextBox>(w, t => t.IsVisible && t.Name != "AddBox" && ReferenceEquals(t.DataContext, dataContext))
        ?? throw new InvalidOperationException("No visible edit box");

    // ================= Simulated input =================

    /// <summary>Presses a key on an element like the keyboard does (preview event, then the normal one).</summary>
    protected void Press(IInputElement target, Key key, ModifierKeys mods = ModifierKeys.None)
    {
        Held = mods;
        var source = PresentationSource.FromVisual((Visual)target) ?? PresentationSource.CurrentSources.Cast<PresentationSource>().First();
        // Alt+key and F10 arrive as Key.System with the real key in SystemKey
        bool system = mods.HasFlag(ModifierKeys.Alt) || key == Key.F10;
        KeyEventArgs Make(RoutedEvent e)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, system ? Key.System : key) { RoutedEvent = e };
            if (system) typeof(KeyEventArgs).GetField("_realKey", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(args, key);
            return args;
        }
        var preview = Make(Keyboard.PreviewKeyDownEvent);
        target.RaiseEvent(preview);
        if (!preview.Handled) target.RaiseEvent(Make(Keyboard.KeyDownEvent));
        Held = ModifierKeys.None;
        Pump();
    }

    /// <summary>Presses a key on whatever has keyboard focus.</summary>
    protected void Press(Key key, ModifierKeys mods = ModifierKeys.None) =>
        Press(Keyboard.FocusedElement ?? throw new InvalidOperationException("Nothing has focus"), key, mods);

    /// <summary>
    /// Clicks a button the way a user (or a screen reader) does: through its real click handling, which also
    /// runs commands like closing a dialog for a Cancel button. Raising the Click event alone would skip those.
    /// </summary>
    protected static void Click(Button button)
    {
        var invoke = (System.Windows.Automation.Provider.IInvokeProvider)
            new System.Windows.Automation.Peers.ButtonAutomationPeer(button)
                .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke);
        invoke.Invoke();
        Pump();
    }

    protected static MouseButtonEventArgs MouseButton(UIElement target, RoutedEvent e, MouseButton button = System.Windows.Input.MouseButton.Left, int clicks = 1)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, button) { RoutedEvent = e, Source = target };
        typeof(MouseButtonEventArgs).GetProperty("ClickCount")!.GetSetMethod(true)!.Invoke(args, new object[] { clicks });
        return args;
    }

    /// <summary>Left mouse button down on an element (the preview and the normal event), with 1 or 2 clicks.</summary>
    protected static void MouseDown(UIElement target, int clicks = 1)
    {
        // like real input: the general mouse-down events, which WPF turns into the left-button ones
        var preview = MouseButton(target, Mouse.PreviewMouseDownEvent, clicks: clicks);
        target.RaiseEvent(preview);
        if (!preview.Handled) target.RaiseEvent(MouseButton(target, Mouse.MouseDownEvent, clicks: clicks));
        Pump();
    }

    protected static void RightClick(UIElement target)
    {
        target.RaiseEvent(MouseButton(target, UIElement.MouseRightButtonUpEvent, System.Windows.Input.MouseButton.Right));
        Pump();
    }

    protected static MouseEventArgs MouseMove(UIElement target) =>
        new(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent, Source = target };

    // ================= Modal dialogs =================

    static readonly Queue<(Type type, Action<Window> act)> PendingDialogs = new();
    static bool _loadedHooked;

    /// <summary>
    /// Runs <paramref name="act"/> on the next window of type T once it has opened. Modal dialogs block until
    /// they close, so a test schedules what to do in them before opening them.
    /// </summary>
    protected static void WhenOpened<T>(Action<T> act) where T : Window
    {
        if (!_loadedHooked)
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
            _loadedHooked = true;
        }
        PendingDialogs.Enqueue((typeof(T), w => act((T)w)));
    }

    static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window w || PendingDialogs.Count == 0 || !PendingDialogs.Peek().type.IsInstanceOfType(w)) return;
        var (_, act) = PendingDialogs.Dequeue();
        w.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            try { act(w); }
            catch (Exception ex)
            {
                // a failed check inside a modal dialog would otherwise leave it open and the test hanging
                UiHost.UnhandledErrors.Enqueue(ex);
                if (w.IsVisible) w.Close();
            }
        });
    }

    // ================= Menus =================

    /// <summary>The context menu that is open right now.</summary>
    protected static ContextMenu OpenMenu()
    {
        Pump();
        return OpenMenus().LastOrDefault() ?? throw new InvalidOperationException("No open menu");
    }

    /// <summary>All open context menus, oldest first.</summary>
    static List<ContextMenu> OpenMenus() =>
        PresentationSource.CurrentSources.OfType<HwndSource>()
            .Select(s => s.RootVisual as DependencyObject)
            .Select(root => root as ContextMenu ?? (root == null ? null : Find<ContextMenu>(root)))
            .Where(m => m is { IsOpen: true })
            .Cast<ContextMenu>()
            .ToList();

    protected static MenuItem MenuItem(ItemsControl menu, params string[] path)
    {
        ItemsControl current = menu;
        MenuItem? found = null;
        foreach (var header in path)
        {
            found = current.Items.OfType<MenuItem>().FirstOrDefault(m => (string)m.Header == header)
                    ?? throw new InvalidOperationException($"No menu item '{header}' in: " +
                                                           string.Join(", ", current.Items.OfType<MenuItem>().Select(m => m.Header)));
            current = found;
        }
        return found!;
    }

    /// <summary>Clicks a menu item (path: "Window color", "Yellow") and lets its deferred action run.</summary>
    protected static void ChooseFromMenu(ContextMenu menu, params string[] path)
    {
        var item = MenuItem(menu, path);
        menu.IsOpen = false;
        item.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent, item));
        Pump();
    }
}
