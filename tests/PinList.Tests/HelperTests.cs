using Avalonia.Headless.XUnit;
using Avalonia;
using PinList;
using Xunit;

namespace PinList.Tests;

public class DisplaysTests
{
    // A big monitor on the left and a smaller one on the right, aligned at the top
    static readonly Rect[] Monitors = { new(0, 0, 2560, 1400), new(2560, 0, 1920, 1040) };

    [AvaloniaFact]
    public void A_widget_on_a_monitor_is_reachable() =>
        Assert.True(Displays.IsReachable(new Rect(100, 100, 320, 480), Monitors));

    [AvaloniaFact]
    public void A_widget_in_the_corner_no_monitor_covers_is_not_reachable() =>
        // inside the rectangle around both monitors, but below the smaller one
        Assert.False(Displays.IsReachable(new Rect(3000, 1200, 320, 480), Monitors));

    [AvaloniaFact]
    public void A_widget_mostly_off_screen_needs_60_visible_pixels_of_header()
    {
        Assert.True(Displays.IsReachable(new Rect(-250, 10, 320, 480), Monitors));  // 70 px visible
        Assert.False(Displays.IsReachable(new Rect(-280, 10, 320, 480), Monitors)); // 40 px visible
    }

    [AvaloniaFact]
    public void A_widget_whose_header_is_below_the_work_area_is_not_reachable() =>
        Assert.False(Displays.IsReachable(new Rect(100, 1395, 320, 480), Monitors));
}

public class SyncIconTests
{
    static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(PinList.Sync.SyncStatus.Local, null, "Icon.CloudOff", "Not synced", false)]
    [InlineData(PinList.Sync.SyncStatus.UpToDate, 0, "Icon.CloudCheck", "Synced · just now", false)]
    [InlineData(PinList.Sync.SyncStatus.UpToDate, 5, "Icon.CloudCheck", "Synced · 5 min ago", false)]
    [InlineData(PinList.Sync.SyncStatus.UpToDate, 125, "Icon.CloudCheck", "Synced · 2 h ago", false)]
    [InlineData(PinList.Sync.SyncStatus.UpToDate, 60 * 24, "Icon.CloudCheck", "Synced · 1 day ago", false)]
    [InlineData(PinList.Sync.SyncStatus.UpToDate, 60 * 72, "Icon.CloudCheck", "Synced · 3 days ago", false)]
    [InlineData(PinList.Sync.SyncStatus.UpToDate, -1, "Icon.CloudCheck", "Synced", false)]
    [InlineData(PinList.Sync.SyncStatus.Syncing, 1, "Icon.Sync", "Syncing…", false)]
    [InlineData(PinList.Sync.SyncStatus.Waiting, 1, "Icon.CloudWaiting", "Offline · changes will sync later", false)]
    [InlineData(PinList.Sync.SyncStatus.Problem, 1, "Icon.CloudProblem", "Google signed you out.", true)]
    public void Each_status_has_its_icon_and_tooltip(PinList.Sync.SyncStatus status, int? minutesAgo, string icon, string tip, bool danger)
    {
        DateTime? last = minutesAgo is null or < 0 ? null : Now.AddMinutes(-minutesAgo.Value);
        var problem = status == PinList.Sync.SyncStatus.Problem ? "Google signed you out." : null;
        Assert.Equal((icon, tip, danger), SyncIcon.For(new PinList.Sync.ListSyncState(status, last, problem), Now));
    }

    [Fact]
    public void Signed_out_has_no_icon() =>
        Assert.Null(SyncIcon.For(new PinList.Sync.ListSyncState(PinList.Sync.SyncStatus.Hidden, null, null), Now));

    [Fact]
    public void Last_sync_in_words()
    {
        Assert.Equal("never", SyncIcon.Ago(null, Now));
        Assert.Equal("just now", SyncIcon.Ago(Now.AddSeconds(30), Now)); // a clock a bit ahead
        Assert.Equal("1 min ago", SyncIcon.Ago(Now.AddMinutes(-1), Now));
        Assert.Equal("1 h ago", SyncIcon.Ago(Now.AddMinutes(-60), Now));
    }
}

public class AndroidManifestTests
{
    static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PinList.sln"))) dir = dir.Parent;
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    [Fact]
    public void The_phone_app_may_use_the_internet_for_sync()
    {
        // Release builds get only the permissions listed here; without this one every sync and sign-in call fails
        var manifest = File.ReadAllText(RepoFile("src", "PinList.Android", "Properties", "AndroidManifest.xml"));
        Assert.Contains("android.permission.INTERNET", manifest);
        Assert.Contains("android.permission.ACCESS_NETWORK_STATE", manifest);
    }
}
