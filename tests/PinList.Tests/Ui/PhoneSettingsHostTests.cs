using Avalonia.Headless.XUnit;
using PinList.Sync;
using PinList.Tests.Sync;
using Xunit;

namespace PinList.Tests;

/// <summary>What the phone's Settings tab offers: the choices the old ⋮ menu had.</summary>
public partial class PhoneTests
{
    PhoneSettingsHost SettingsHost(Action<Action>? confirm = null) => new(_view, confirm ?? (run => run()));

    static void WaitFor(Task t)
    {
        Assert.True(UiHost.WaitUntil(() => t.IsCompleted));
        t.GetAwaiter().GetResult();
    }

    [AvaloniaFact]
    public void Settings_host_theme_goes_to_the_app()
    {
        Show();
        var host = SettingsHost();
        var real = PhoneView.SetTheme;
        string? set = null;
        PhoneView.SetTheme = key => set = key;
        try
        {
            Store.Data.Settings.Theme = "Light";
            Assert.Equal("Light", host.Theme);
            host.SetTheme("Dark");
            Assert.Equal("Dark", set);
        }
        finally { PhoneView.SetTheme = real; }
    }

    [AvaloniaFact]
    public void Settings_host_hides_what_the_phone_does_not_have()
    {
        Show();
        var host = SettingsHost();
        Assert.Null(host.Sync);
        Assert.Null(host.UnavailableSyncText);
        Assert.Null(host.ReminderProblem); // Android shows its own notification hints
        host.OpenNotificationSettings();   // nothing to open
        Assert.Null(host.UpdatesOn);
        Assert.Null(host.AutostartText);
        Assert.False(host.AutostartOn);
        Assert.False(host.CanChangeAutostart);
        host.SetAutostart(true);
        Assert.False(host.HasDataFolder);
        host.OpenDataFolder();
        Assert.Null(host.ExitText);
        host.Exit();
        host.SetUpdates(true); // no update check: nothing to turn on
        Assert.Equal("PinList", host.VersionText);
    }

    [AvaloniaFact]
    public void Settings_host_send_feedback_opens_the_form_in_the_browser()
    {
        Show();
        var host = SettingsHost();
        var real = PhoneView.OpenUrl;
        var opened = new List<string>();
        PhoneView.OpenUrl = (_, url) => { opened.Add(url); return Task.CompletedTask; };
        try
        {
            host.SendFeedback(); // no update check: the app's own version
            var version = typeof(PhoneView).Assembly.GetName().Version!.ToString(3);
            Assert.Equal($"https://filipraic.github.io/pin-list/feedback?v={version}&os=Android", opened[0]);

            _view.UpdateCheck = new Updates(_ => Task.FromResult<UpdateInfo?>(null), new Version(2, 2, 0));
            PhoneApp.SystemName = "Android 15 (Pixel 9)"; // set by the Android app
            host.SendFeedback();
            Assert.Equal("https://filipraic.github.io/pin-list/feedback?v=2.2.0&os=Android%2015%20%28Pixel%209%29", opened[1]);
        }
        finally
        {
            PhoneView.OpenUrl = real;
            PhoneApp.SystemName = "Android";
        }
    }

    [AvaloniaFact]
    public void Settings_host_explains_a_phone_without_sync()
    {
        _phoneAuth.IsAvailable = false;
        ShowWithSync();
        var host = SettingsHost();
        Assert.Null(host.Sync);
        Assert.Equal("Sync isn't available on this phone.", host.UnavailableSyncText);
    }

    [AvaloniaFact]
    public void Settings_host_signs_in_syncs_and_signs_out()
    {
        ShowWithSync();
        string? asked = null;
        var host = SettingsHost(run => { asked = "asked"; run(); });
        int changes = 0;
        host.Changed += () => changes++;

        var out_ = host.Sync!;
        Assert.Null(out_.Account);
        Assert.Equal("Sign in with Google", Assert.Single(out_.Actions).Text);
        out_.Actions[0].Run();
        Assert.True(UiHost.WaitUntil(() => _view.Sync!.Account != null));
        Assert.True(UiHost.WaitUntil(() => changes > 0));

        var summary = host.Sync!;
        Assert.Equal("me@example.com", summary.Account);
        Assert.Null(summary.Problem);
        Assert.StartsWith("Synced ", summary.Status);
        Assert.Equal(new[] { "Sync now", "Sign out", "Sign out and delete cloud data" }, summary.Actions.Select(a => a.Text));

        int calls = _phoneCloud.Calls;
        summary.Actions[0].Run();
        Assert.True(UiHost.WaitUntil(() => _phoneCloud.Calls > calls));

        summary.Actions[1].Run();
        Assert.True(UiHost.WaitUntil(() => _view.Sync!.Account == null));

        host.Sync!.Actions[0].Run();
        Assert.True(UiHost.WaitUntil(() => _view.Sync!.Account != null));
        host.Sync!.Actions[2].Run(); // the question is asked first
        Assert.Equal("asked", asked);
        Assert.True(UiHost.WaitUntil(() => _view.Sync!.Account == null));
        Assert.Equal(new[] { "reset.json" }, _phoneCloud.Names);
    }

    [AvaloniaFact]
    public void Settings_host_offers_signing_in_again_after_google_signed_out()
    {
        ShowWithSync();
        var host = SettingsHost();
        host.Sync!.Actions[0].Run();
        Assert.True(UiHost.WaitUntil(() => _view.Sync!.Account != null));
        _phoneAuth.TokenValid = false;
        WaitFor(_view.Sync!.SyncNow());

        var summary = host.Sync!;
        Assert.Equal("Google signed you out. Sign in again to keep syncing.", summary.Problem);
        Assert.Equal("Sign in again", summary.Actions[0].Text);
        summary.Actions[0].Run();
        Assert.True(UiHost.WaitUntil(() => _phoneAuth.SignIns >= 2));
    }

    [AvaloniaFact]
    public void Settings_host_update_check_and_version()
    {
        Show();
        var host = SettingsHost();
        int changes = 0;
        host.Changed += () => changes++;
        var updates = new Updates(_ => Task.FromResult<UpdateInfo?>(null), new Version(2, 4, 0));
        _view.UpdateCheck = updates;
        Assert.False(host.UpdatesOn);
        Assert.Equal("You're on 2.4.0", host.VersionText);
        host.SetUpdates(true);
        Assert.True(updates.Enabled);
        Assert.True(host.UpdatesOn);
        Assert.True(UiHost.WaitUntil(() => host.VersionText == "You're on 2.4.0, the newest"));
        Assert.True(changes > 0);

        var found = new Updates(_ => Task.FromResult<UpdateInfo?>(new UpdateInfo(new Version(2, 5, 0))), new Version(2, 4, 0));
        _view.UpdateCheck = found;
        found.Enabled = true;
        WaitFor(found.CheckNow());
        Assert.Equal("PinList 2.5.0 is out", host.VersionText);
        host.RaiseChanged();
        _view.UpdateCheck = null;
    }

    [AvaloniaFact]
    public void Settings_host_exports_and_imports()
    {
        MakeBoard(("A", new[] { "a" }));
        Show();
        var host = SettingsHost();
        var realExport = PhoneView.PickExportFile;
        var realImport = PhoneView.PickImportFile;
        var exported = new MemoryStream();
        try
        {
            PhoneView.PickExportFile = _ => Task.FromResult<Stream?>(new NonClosingStream(exported));
            WaitFor(host.Export());
            Assert.True(exported.Length > 0);
            PhoneView.PickImportFile = _ => Task.FromResult<Stream?>(null);
            WaitFor(host.Import());
        }
        finally
        {
            PhoneView.PickExportFile = realExport;
            PhoneView.PickImportFile = realImport;
        }
    }

    [AvaloniaFact]
    public void Settings_host_follows_a_replaced_sync_service_and_update_check()
    {
        ShowWithSync();
        var host = SettingsHost();
        Assert.NotNull(host.Sync);
        _view.Sync = new SyncService(new FakeAuth(), () => _phoneCloud, "Other phone");
        Assert.NotNull(host.Sync);
        _view.Sync = null;
        Assert.Null(host.Sync);
        Assert.Null(host.UnavailableSyncText);

        _view.UpdateCheck = new Updates(_ => Task.FromResult<UpdateInfo?>(null), new Version(2, 4, 0));
        Assert.False(host.UpdatesOn);
        _view.UpdateCheck = new Updates(_ => Task.FromResult<UpdateInfo?>(null), new Version(2, 4, 0));
        Assert.False(host.UpdatesOn);
        _view.UpdateCheck = null;
        Assert.Null(host.UpdatesOn);
    }

    /// <summary>A stream that stays readable after the code under test disposed it.</summary>
    sealed class NonClosingStream : Stream
    {
        readonly Stream _inner;
        public NonClosingStream(Stream inner) => _inner = inner;
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) { }
    }
}
