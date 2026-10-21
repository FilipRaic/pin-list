using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PinList;
using Xunit;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using WinForms = System.Windows.Forms;

namespace PinList.Tests;

public class NativeTests : UiTest
{
    static Window NewWindow() => new() { Width = 100, Height = 100, Left = 10, Top = 10, ShowActivated = false };

    [Fact]
    public void Tool_window_style_title_bar_and_foreground()
    {
        Ui(() =>
        {
            var unshown = NewWindow();
            Native.MakeToolWindow(unshown); // no window handle yet: nothing to do
            Native.StyleTitleBar(unshown, true, Colors.Black);
            Native.BringToForeground(unshown);

            var w = NewWindow();
            w.Show();
            Assert.Equal(0, Native.GetExStyle(w) & Native.ToolWindowStyle);
            Native.MakeToolWindow(w);
            Assert.NotEqual(0, Native.GetExStyle(w) & Native.ToolWindowStyle);
            Native.StyleTitleBar(w, true, Colors.Black);
            Native.StyleTitleBar(w, false, Colors.White);
            Native.BringToForeground(w);
            Native.AllowAnyForeground();
            _ = Native.CursorScreenPosition();
            w.Close();
        });
    }

    [Fact]
    public void Widgets_belong_to_the_desktop_window()
    {
        Ui(() =>
        {
            var real = Native.FindDesktopWindow;
            try
            {
                Assert.Equal(IntPtr.Zero, real()); // no desktop on the hidden test desktop
                var desktop = NewWindow();
                desktop.Show();
                var handle = new WindowInteropHelper(desktop).Handle;
                Native.FindDesktopWindow = () => handle;

                var w = NewWindow();
                Native.PinToDesktop(w);
                Assert.Equal(handle, new WindowInteropHelper(w).Owner);

                Native.FindDesktopWindow = () => IntPtr.Zero;
                var lone = NewWindow();
                Native.PinToDesktop(lone);
                Assert.Equal(IntPtr.Zero, new WindowInteropHelper(lone).Owner);
                desktop.Close();
            }
            finally { Native.FindDesktopWindow = real; }
        });
    }
}

public class A11yTests : UiTest
{
    [Fact]
    public void Announce_ignores_nothing_to_say_and_never_throws()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new string[0]));
            A11y.Announce(w, "hello");
            A11y.Announce(null, "nobody to say it");
            A11y.Announce(w, " ");
        });

        // an element of another thread can't be asked; the announcement is skipped
        UIElement? foreign = null;
        var t = new Thread(() => foreign = new Border());
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        Ui(() => A11y.Announce(foreign, "from the wrong thread"));
    }

    [Fact]
    public void Text_scale_applies_to_framework_content_only()
    {
        Ui(() =>
        {
            var w = new Window { Content = "just text" };
            A11y.ApplyTextScale(w); // nothing to scale

            var withContent = new Window { Content = new Border() };
            A11y.ApplyTextScale(withContent);
            Assert.Same(Transform.Identity, ((Border)withContent.Content).LayoutTransform);

            A11y.TextScaleSource = () => 1.75;
            Assert.True(A11y.RefreshTextScale());
            Assert.False(A11y.RefreshTextScale()); // unchanged
            A11y.ApplyTextScale(withContent);
            Assert.Equal(1.75, ((ScaleTransform)((Border)withContent.Content).LayoutTransform).ScaleX);
        });
    }

    [Fact]
    public void Rows_are_list_items_and_categories_expand_and_collapse()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", new[] { "a" }));
            var cat = w.Board.Categories[0];
            var itemPeer = UIElementAutomationPeer.CreatePeerForElement(Row(w, cat.Items[0]));
            Assert.Equal(AutomationControlType.ListItem, itemPeer.GetAutomationControlType());
            Assert.Equal("PinListRow", itemPeer.GetClassName());
            Assert.True(itemPeer.IsControlElement());
            Assert.True(itemPeer.IsContentElement());
            Assert.True(itemPeer.IsKeyboardFocusable());
            Assert.Null(itemPeer.GetPattern(PatternInterface.ExpandCollapse)); // items don't expand
            Assert.Null(itemPeer.GetPattern(PatternInterface.Invoke));

            var catPeer = UIElementAutomationPeer.CreatePeerForElement(Row(w, cat));
            var expand = (IExpandCollapseProvider)catPeer.GetPattern(PatternInterface.ExpandCollapse);
            Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
            expand.Collapse();
            Assert.True(cat.Collapsed);
            Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
            expand.Collapse(); // already: nothing
            expand.Expand();
            Assert.False(cat.Collapsed);

            var custom = new AccessibleRow { ControlType = AutomationControlType.TreeItem, Tag = "not a category" };
            var customPeer = UIElementAutomationPeer.CreatePeerForElement(custom);
            Assert.Equal(AutomationControlType.TreeItem, customPeer.GetAutomationControlType());
            Assert.Null(customPeer.GetPattern(PatternInterface.ExpandCollapse));
            ((IExpandCollapseProvider)catPeer).Expand(); // and via a non-category row it would do nothing:
            typeof(AccessibleRow).GetNestedType("RowPeer", BindingFlags.NonPublic)!
                .GetMethod("Expand")!.Invoke(customPeer, null);
            Assert.Equal(ExpandCollapseState.Expanded, ((IExpandCollapseProvider)customPeer).ExpandCollapseState);
        });
    }
}

public class ThemeTests : UiTest
{
    static Color C(ResourceDictionary r, string key) => ((SolidColorBrush)r[key]).Color;

    static double Luminance(Color c)
    {
        static double L(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
    }

    /// <summary>Contrast of a (possibly see-through) text color on a solid background, as WCAG measures it.</summary>
    static double Contrast(Color text, Color bg)
    {
        double a = text.A / 255.0;
        var mixed = Color.FromRgb((byte)(bg.R + (text.R - bg.R) * a), (byte)(bg.G + (text.G - bg.G) * a), (byte)(bg.B + (text.B - bg.B) * a));
        double l1 = Luminance(mixed), l2 = Luminance(bg);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    public static IEnumerable<object[]> Styles => Themes.BoardStyles.Select(s => new object[] { s.Key });

    [Theory]
    [MemberData(nameof(Styles))]
    public void Widget_text_meets_WCAG_AA_on_every_color(string style)
    {
        Ui(() =>
        {
            var r = new ResourceDictionary();
            Themes.ApplyBoard(r, Themes.Get(style), transparent: false);
            var bg = C(r, "BoardBg");
            foreach (var key in new[] { "TextPrimary", "TextSecondary", "TextMuted" })
                Assert.True(Contrast(C(r, key), bg) >= 4.5, $"{key} on {style}: {Contrast(C(r, key), bg):0.00}");
            Assert.True(Contrast(C(r, "Overdue"), bg) >= 4.5, $"Overdue on {style}");
            Assert.True(Contrast(C(r, "Accent"), bg) >= 4.5, $"Accent on {style}");
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Menus_dialogs_and_archive_meet_WCAG_AA(bool dark)
    {
        Ui(() =>
        {
            var r = new ResourceDictionary();
            Themes.ApplyChrome(r, dark);
            Assert.True(Contrast(C(r, "MenuText"), C(r, "MenuBg")) >= 4.5);
            Assert.True(Contrast(C(r, "MenuTextMuted"), C(r, "MenuBg")) >= 4.5);
            Assert.True(Contrast(C(r, "ArcText"), C(r, "ArcSurface")) >= 4.5);
            Assert.True(Contrast(C(r, "ArcMuted"), C(r, "ArcBg")) >= 4.5);
            Assert.True(Contrast(C(r, "AccentText"), C(r, "AccentFill")) >= 4.5);
            Assert.True(Contrast(C(r, "DangerText"), C(r, "DangerFill")) >= 4.5);
        });
    }

    [Fact]
    public void Transparent_widgets_are_slightly_see_through()
    {
        Ui(() =>
        {
            var r = new ResourceDictionary();
            Themes.ApplyBoard(r, Themes.Get("Light"), transparent: true);
            Assert.Equal(0xE6, C(r, "BoardBg").A);
            Assert.True(((SolidColorBrush)r["BoardBg"]).IsFrozen);
        });
    }

    [Fact]
    public void Contrast_themes_use_the_Windows_colors()
    {
        Ui(() =>
        {
            Themes.ForceHighContrast = true;
            var board = new ResourceDictionary();
            Themes.ApplyBoard(board, Themes.Get("Yellow"), transparent: true);
            Assert.Same(SystemColors.WindowBrush, board["BoardBg"]);
            Assert.Same(SystemColors.WindowTextBrush, board["TextMuted"]);
            Assert.Same(SystemColors.HotTrackBrush, board["Overdue"]);

            var chrome = new ResourceDictionary();
            Themes.ApplyChrome(chrome, dark: false);
            Assert.Same(SystemColors.HighlightBrush, chrome["MenuHover"]);
            Assert.Same(SystemColors.HighlightTextBrush, chrome["MenuHoverText"]);
            Assert.Same(SystemColors.HighlightTextBrush, chrome["ArcSelectedText"]);
        });
    }

    [Fact]
    public void Theme_settings_and_styles()
    {
        Assert.Equal("Dark", Themes.Get("no such style").Key);
        Assert.Equal("Peach", Themes.Get("Peach").Name);
        Assert.False(Themes.IsDark("Light"));
        Assert.True(Themes.IsDark("Dark"));
        Ui(() =>
        {
            Themes.SystemDarkSource = () => false;
            Assert.False(Themes.IsDark("System"));
            Assert.False(Themes.SystemUsesDark());
            Themes.SystemDarkSource = () => true;
            Assert.True(Themes.IsDark("anything else follows Windows"));
        });
    }
}

public class ScreenAndInputTests : UiTest
{
    [Fact]
    public void Work_areas_of_the_monitors()
    {
        Ui(() =>
        {
            var areas = Screens.WorkAreas();
            Assert.NotEmpty(areas);
            var primary = Screens.Primary;
            var center = new Point(primary.Left + primary.Width / 2, primary.Top + primary.Height / 2);
            Assert.Contains(Screens.WorkAreaAt(center), areas);
            Assert.Contains(Screens.WorkAreaAtCursor(), areas);
        });
    }

    [Fact]
    public void Real_input_is_read_when_not_simulated()
    {
        Ui(() =>
        {
            InputState.Reset();
            _ = InputState.Modifiers();
            _ = InputState.ShiftHeld;
            var w = OpenNew(("A", new string[0]));
            var args = new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent };
            _ = InputState.IsLeftButtonPressed(args);
            _ = InputState.Position(args, w);
            _ = InputState.CursorScreen();
        });
    }

    [Fact]
    public void Date_dialog_uses_the_real_clock_by_default()
    {
        Ui(() => Assert.InRange(DateDialog.SystemClock(), DateTime.Now.AddMinutes(-1), DateTime.Now.AddMinutes(1)));
    }
}

public class ReminderServiceTests : UiTest
{
    [Fact]
    public void Start_checks_right_away_and_then_on_a_timer()
    {
        Ui(() =>
        {
            var board = MakeBoard(("A", new[] { "a", "b" }));
            var (a, b) = (board.Categories[0].Items[0], board.Categories[0].Items[1]);
            var now = new DateTime(2030, 1, 1, 9, 0, 0);
            var reminders = new Reminders { Clock = () => now, Interval = TimeSpan.FromMilliseconds(30) };
            var due = new List<IReadOnlyList<TodoItem>>();
            reminders.Due += due.Add;

            a.ReminderAt = now.AddMinutes(-1); // missed while PinList was closed
            b.ReminderAt = now.AddMinutes(1);
            reminders.Start();
            Assert.True(reminders.IsRunning);
            Assert.Equal(new[] { a }, due.Single());
            Assert.True(Saver.Requests > 0); // "shown" is saved

            now = now.AddMinutes(2);
            Assert.True(UiHost.WaitUntil(() => due.Count == 2)); // the timer found b
            Assert.Equal(new[] { b }, due[1]);

            reminders.Stop();
            Assert.False(reminders.IsRunning);
        });
    }

    [Fact]
    public void After_midnight_today_and_overdue_labels_are_refreshed()
    {
        Ui(() =>
        {
            var item = MakeBoard(("A", new[] { "a" })).Categories[0].Items[0];
            item.DueDate = DateTime.Today;
            var now = DateTime.Today.AddHours(23);
            var reminders = new Reminders { Clock = () => now };
            var changed = new List<string?>();
            item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            reminders.Check();
            Assert.Empty(changed); // same day

            now = now.AddHours(2); // past midnight
            reminders.Check();
            Assert.Contains(nameof(TodoItem.IsOverdue), changed);
            Assert.Contains(nameof(TodoItem.DueText), changed);
        });
    }
}

public class TrayIconTests : UiTest
{
    [Fact]
    public void Clicks_and_notifications()
    {
        Ui(() =>
        {
            // not visible: nothing appears in the real taskbar
            using var tray = new TrayIcon(System.Drawing.SystemIcons.Application, visible: false);
            var events = new List<string>();
            tray.LeftClick += () => events.Add("left");
            tray.RightClick += () => events.Add("right");
            tray.NotificationClicked += () => events.Add("notification");

            tray.OnMouseUp(WinForms.MouseButtons.Left);
            tray.OnMouseUp(WinForms.MouseButtons.Right);
            tray.OnMouseUp(WinForms.MouseButtons.Middle); // ignored

            // what Windows does when the icon is clicked or a notification is clicked
            var icon = (WinForms.NotifyIcon)typeof(TrayIcon).GetField("_icon", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tray)!;
            typeof(WinForms.NotifyIcon).GetMethod("OnMouseUp", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(icon, new object[] { new WinForms.MouseEventArgs(WinForms.MouseButtons.Left, 1, 0, 0, 0) });
            typeof(WinForms.NotifyIcon).GetMethod("OnBalloonTipClicked", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(icon, null);

            tray.ShowNotification("Reminder", "Call the bank");
            Assert.Equal(new[] { "left", "right", "left", "notification" }, events);
        });
    }

    [Fact]
    public void Clicks_without_listeners_do_nothing()
    {
        Ui(() =>
        {
            using var tray = new TrayIcon(System.Drawing.SystemIcons.Application, visible: false);
            tray.OnMouseUp(WinForms.MouseButtons.Left);
            tray.OnMouseUp(WinForms.MouseButtons.Right);
        });
    }
}
