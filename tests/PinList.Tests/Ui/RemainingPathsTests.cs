using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PinList;
using Xunit;
using Point = System.Windows.Point;

namespace PinList.Tests;

/// <summary>The less common paths: the real save timer, empty data on start, odd icon addresses, and so on.</summary>
public class RemainingPathsTests : UiTest
{
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);

    [Fact]
    public void The_save_timer_saves_once_after_a_burst_of_changes()
    {
        Ui(() =>
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
        });
    }

    [Fact]
    public void Starting_with_data_that_has_no_widgets_creates_one()
    {
        Ui(() =>
        {
            App.Stop();
            File.WriteAllText(Path.Combine(UiHost.DataDir, "data.json"), """{ "version": 2, "boards": [] }""");
            Tray.Disposed = false;
            Assert.True(App.Start());
            Assert.Single(Store.Data.Boards);
            Assert.Single(App.OpenWindows);
        });
    }

    [Fact]
    public void An_invalid_icon_address_falls_back_to_the_standard_icon()
    {
        Ui(() =>
        {
            var real = PinList.App.TrayIconUri;
            PinList.App.TrayIconUri = "not an address";
            try
            {
                Assert.Equal(System.Drawing.SystemIcons.Application.Size, PinList.App.LoadTrayIcon().Size);
                Assert.Contains("Tray icon:", File.ReadAllText(Path.Combine(Dir, "log.txt")));
            }
            finally { PinList.App.TrayIconUri = real; }
        });
    }

    [Fact]
    public void A_date_dialog_over_a_hidden_widget_opens_in_the_middle_of_the_screen()
    {
        Ui(() =>
        {
            var owner = OpenNew(("A", new string[0]));
            owner.Hide();
            WhenOpened<DateDialog>(d =>
            {
                Assert.Null(d.Owner);
                Assert.Equal(WindowStartupLocation.CenterScreen, d.WindowStartupLocation);
                d.Close();
            });
            new WindowsDialogs().PickDueDate(owner, null);
        });
    }

    [Fact]
    public void Auto_scroll_skips_a_widget_that_Windows_just_destroyed()
    {
        Ui(() =>
        {
            var w = OpenNew(("A", Enumerable.Range(1, 40).Select(i => "item " + i).ToArray()));
            w.Height = 250;
            Pump();
            var screen = w.PointToScreen(new Point(20, 20));
            DestroyWindow(new WindowInteropHelper(w).Handle);
            // no longer visible: skipped without asking where it is on screen
            typeof(BoardWindow).GetMethod("AutoScrollAt", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(w, new object[] { screen });
            Pump();
        });
    }
}

public class MoreReminderTests
{
    [Fact]
    public void Several_due_reminders_come_in_time_order()
    {
        var now = new DateTime(2030, 1, 1, 12, 0, 0);
        var later = new TodoItem { Text = "later", ReminderAt = now.AddMinutes(-1) };
        var earlier = new TodoItem { Text = "earlier", ReminderAt = now.AddMinutes(-30) };
        Assert.Equal(new[] { earlier, later }, Reminders.TakeDue(new[] { later, earlier }, now));
    }
}
