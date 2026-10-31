using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Xunit;
using WinForms = System.Windows.Forms;

namespace PinList.Tests;

public class A11yTests : UiTest
{
    [AvaloniaFact]
    public void Announce_says_it_through_the_windows_live_region()
    {
        var w = OpenNew(("A", new string[0]));
        var region = Find<LiveRegion>(w)!;
        A11y.Announce(w, "hello");
        Assert.Equal("hello", region.Text);
        Assert.Equal(Avalonia.Automation.AutomationLiveSetting.Assertive, Avalonia.Automation.AutomationProperties.GetLiveSetting(region));

        A11y.Announce(null, "nobody to say it");
        A11y.Announce(w, " ");
        Assert.Equal("hello", region.Text);
        A11y.Announce(new Border(), "not in a window"); // nothing to say it with: skipped
        Assert.Equal("hello", region.Text);
    }

    [AvaloniaFact]
    public void Text_scale_is_read_again_on_request()
    {
        A11y.TextScaleSource = () => 1.75;
        Assert.True(A11y.RefreshTextScale());
        Assert.False(A11y.RefreshTextScale()); // unchanged
        Assert.Equal(1.75, A11y.TextScale);
    }

    [AvaloniaFact]
    public void Rows_are_list_items_and_categories_expand_and_collapse()
    {
        var w = OpenNew(("A", new[] { "a" }));
        var cat = w.Board.Categories[0];
        var itemPeer = ControlAutomationPeer.CreatePeerForElement(Row(w, cat.Items[0]));
        Assert.Equal(AutomationControlType.ListItem, itemPeer.GetAutomationControlType());
        Assert.Equal("PinListRow", itemPeer.GetClassName());
        Assert.True(itemPeer.IsControlElement());
        Assert.True(itemPeer.IsContentElement());
        Assert.Null(itemPeer.GetProvider<IExpandCollapseProvider>()); // items don't expand

        var catPeer = ControlAutomationPeer.CreatePeerForElement(Row(w, cat));
        var expand = catPeer.GetProvider<IExpandCollapseProvider>()!;
        Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
        Assert.False(expand.ShowsMenu);
        expand.Collapse();
        Assert.True(cat.Collapsed);
        Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
        expand.Collapse(); // already: nothing
        expand.Expand();
        Assert.False(cat.Collapsed);

        var custom = new AccessibleRow { ControlType = AutomationControlType.TreeItem, Tag = "not a category" };
        var customPeer = ControlAutomationPeer.CreatePeerForElement(custom);
        Assert.Equal(AutomationControlType.TreeItem, customPeer.GetAutomationControlType());
        Assert.Null(customPeer.GetProvider<IExpandCollapseProvider>());

        Row(w, cat).Tag = new Category(); // a recycled row whose category isn't in any widget: no change saved
        expand.Collapse();
    }
}

public class ThemeTests : UiTest
{
    static Color C(IResourceDictionary r, string key) => ((ImmutableSolidColorBrush)r[key]!).Color;

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

    public static TheoryData<string> Styles => new(Themes.BoardStyles.Select(s => s.Key));

    [AvaloniaTheory]
    [MemberData(nameof(Styles))]
    public void Widget_text_meets_WCAG_AA_on_every_color(string style)
    {
        var r = new ResourceDictionary();
        Themes.ApplyBoard(r, Themes.Get(style), transparent: false);
        var bg = C(r, "BoardBg");
        foreach (var key in new[] { "TextPrimary", "TextSecondary", "TextMuted" })
            Assert.True(Contrast(C(r, key), bg) >= 4.5, $"{key} on {style}: {Contrast(C(r, key), bg):0.00}");
        Assert.True(Contrast(C(r, "Overdue"), bg) >= 4.5, $"Overdue on {style}");
        Assert.True(Contrast(C(r, "Accent"), bg) >= 4.5, $"Accent on {style}");
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Menus_dialogs_and_archive_meet_WCAG_AA(bool dark)
    {
        var r = new ResourceDictionary();
        Themes.ApplyChrome(r, dark);
        Assert.True(Contrast(C(r, "MenuText"), C(r, "MenuBg")) >= 4.5);
        Assert.True(Contrast(C(r, "MenuTextMuted"), C(r, "MenuBg")) >= 4.5);
        Assert.True(Contrast(C(r, "ArcText"), C(r, "ArcSurface")) >= 4.5);
        Assert.True(Contrast(C(r, "ArcMuted"), C(r, "ArcBg")) >= 4.5);
        Assert.True(Contrast(C(r, "AccentText"), C(r, "AccentFill")) >= 4.5);
        Assert.True(Contrast(C(r, "DangerText"), C(r, "DangerFill")) >= 4.5);
        Assert.Equal(C(r, "MenuBg"), C(r, "ToolTipBackground")); // tooltips match the menus
    }

    [AvaloniaFact]
    public void Transparent_widgets_are_slightly_see_through()
    {
        var r = new ResourceDictionary();
        Themes.ApplyBoard(r, Themes.Get("Light"), transparent: true);
        Assert.Equal(0xE6, C(r, "BoardBg").A);
    }

    [AvaloniaFact]
    public void Contrast_themes_use_the_system_colors()
    {
        var hc = new ContrastColors(Colors.Black, Colors.White, Colors.Cyan, Colors.Navy, Colors.Yellow);
        Themes.ContrastSource = () => hc;
        Assert.True(Themes.HighContrast);
        var board = new ResourceDictionary();
        Themes.ApplyBoard(board, Themes.Get("Yellow"), transparent: true);
        Assert.Equal(Colors.Black, C(board, "BoardBg"));
        Assert.Equal(Colors.White, C(board, "TextMuted"));
        Assert.Equal(Colors.Yellow, C(board, "Overdue"));

        var chrome = new ResourceDictionary();
        Themes.ApplyChrome(chrome, dark: false);
        Assert.Equal(Colors.Cyan, C(chrome, "MenuHover"));
        Assert.Equal(Colors.Navy, C(chrome, "MenuHoverText"));
        Assert.Equal(Colors.Navy, C(chrome, "ArcSelectedText"));
    }

    [AvaloniaFact]
    public void Theme_settings_and_styles()
    {
        Assert.Equal("Dark", Themes.Get("no such style").Key);
        Assert.Equal("Peach", Themes.Get("Peach").Name);
        Assert.False(Themes.IsDark("Light"));
        Assert.True(Themes.IsDark("Dark"));
        Themes.SystemDarkSource = () => false;
        Assert.False(Themes.IsDark("System"));
        Assert.False(Themes.SystemUsesDark());
        Themes.SystemDarkSource = () => true;
        Assert.True(Themes.IsDark("anything else follows the system"));
        Assert.Equal(Colors.Red, ((ImmutableSolidColorBrush)Themes.Brush("#FF0000")).Color);
    }

    [AvaloniaFact]
    public void The_real_contrast_setting_can_be_read()
    {
        _ = Native.SystemContrastColors(); // whatever this PC is set to; it must not fail
        Assert.InRange(Native.SystemScale(), 1.0, 5.0);
    }
}

public class DisplayTests : UiTest
{
    [AvaloniaFact]
    public void Work_areas_of_the_monitors()
    {
        var areas = Displays.WorkAreas();
        Assert.NotEmpty(areas);
        var primary = Displays.Primary;
        var center = new Point(primary.Left + primary.Width / 2, primary.Top + primary.Height / 2);
        Assert.Contains(Displays.WorkAreaAt(center), areas);
        Assert.Contains(Displays.WorkAreaAtCursor(), areas);
        Assert.Equal(new WorkArea(1, 2, 3, 4), new Rect(1, 2, 3, 4).ToWorkArea());
    }

    [AvaloniaFact]
    public void Positions_are_stored_in_units_of_the_primary_monitor()
    {
        var real = Displays.ScaleSource;
        Displays.ScaleSource = () => 1.5;
        try
        {
            Assert.Equal(new PixelPoint(150, 300), Displays.ToPixels(100, 200));
            Assert.Equal(new Point(100, 200), Displays.FromPixels(new PixelPoint(150, 300)));
        }
        finally { Displays.ScaleSource = real; }
    }

    [AvaloniaFact]
    public void Date_dialog_uses_the_real_clock_by_default() =>
        Assert.InRange(DateDialog.SystemClock(), DateTime.Now.AddMinutes(-1), DateTime.Now.AddMinutes(1));

    [AvaloniaFact]
    public void Windows_without_a_window_handle_are_left_alone()
    {
        // the headless test platform has no HWNDs; none of these may touch a random handle
        var w = OpenNew(("A", new string[0]));
        Native.PinToDesktop(w);
        Native.MakeToolWindow(w);
        Native.BringToForeground(w);
        Native.StyleTitleBar(w, true, Colors.Black);
        Assert.Equal(0, Native.GetExStyle(w));
        Assert.Equal(IntPtr.Zero, Native.GetOwner(w));
        Native.AllowAnyForeground();
    }
}

public class ReminderServiceTests : UiTest
{
    [AvaloniaFact]
    public void Start_checks_right_away_and_then_on_a_timer()
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
    }

    [AvaloniaFact]
    public void The_save_timer_saves_once_after_a_burst_of_changes()
    {
        int saves = 0;
        var saver = new DebouncedSaveScheduler(() => saves++);
        Assert.False(saver.CancelPending()); // nothing scheduled yet

        saver.Schedule();
        saver.Schedule();
        saver.Schedule();
        Assert.True(UiHost.WaitUntil(() => saves == 1));
        UiHost.Wait(TimeSpan.FromMilliseconds(400));
        Assert.Equal(1, saves);

        saver.Schedule();
        Assert.True(saver.CancelPending()); // e.g. Flush: it saves right away instead
        Assert.False(saver.CancelPending());
        UiHost.Wait(TimeSpan.FromMilliseconds(400));
        Assert.Equal(1, saves);
    }
}

public class TrayIconTests : UiTest
{
    [AvaloniaFact]
    public void Clicks_and_notifications()
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
        tray.SetMenu(Array.Empty<TrayEntry>()); // Windows shows the app's own menu instead
        Assert.Equal(new[] { "left", "right", "left", "notification" }, events);
    }

    [AvaloniaFact]
    public void Clicks_without_listeners_do_nothing()
    {
        using var tray = new TrayIcon(System.Drawing.SystemIcons.Application, visible: false);
        tray.OnMouseUp(WinForms.MouseButtons.Left);
        tray.OnMouseUp(WinForms.MouseButtons.Right);
    }

    [AvaloniaFact]
    public void An_invalid_icon_address_falls_back_to_the_standard_icon()
    {
        var real = App.TrayIconUri;
        App.TrayIconUri = "not an address";
        try
        {
            Assert.Equal(System.Drawing.SystemIcons.Application.Size, App.LoadTrayIcon().Size);
            Assert.Contains("Tray icon:", File.ReadAllText(Path.Combine(Dir, "log.txt")));
        }
        finally { App.TrayIconUri = real; }
    }
}

public class LeftoverPathTests : DialogTest
{
    [AvaloniaFact]
    public void A_date_dialog_over_a_hidden_widget_opens_in_the_middle_of_the_screen()
    {
        var owner = OpenNew(("A", new string[0]));
        owner.Hide();
        Run<DateTime?, DateDialog>(() => new AppDialogs().PickDueDate(owner, null), d =>
        {
            Assert.Null(d.Owner);
            Assert.Equal(WindowStartupLocation.CenterScreen, d.WindowStartupLocation);
            d.Close();
        });
    }

    [AvaloniaFact]
    public void Auto_scroll_skips_a_hidden_widget()
    {
        var w = OpenNew(("A", Enumerable.Range(1, 40).Select(i => "item " + i).ToArray()));
        w.Hide();
        BoardWindow.AutoScrollTick(); // no drag: stops right away
        Assert.Equal(0, Named<ScrollViewer>(w, "Scroller").Offset.Y);
    }

    [AvaloniaFact]
    public void The_reminder_popup_shows_and_its_click_runs_the_action()
    {
        bool clicked = false;
        var popup = ReminderPopup.Show("Reminder", "Call the bank", () => clicked = true);
        Pump();
        Assert.True(popup.IsVisible);
        MouseDown(popup);
        Assert.True(clicked);
        Assert.False(popup.IsVisible);
    }

    [AvaloniaFact]
    public void Previewer_constructors_work()
    {
        // the XAML previewer creates the windows without arguments
        new BoardWindow().Close();
        new ConfirmDialog().Close();
        new DateDialog().Close();
    }
}
