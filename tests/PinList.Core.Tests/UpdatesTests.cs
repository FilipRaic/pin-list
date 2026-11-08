using System.Net;
using PinList;
using PinList.Tests.Sync;
using Xunit;

namespace PinList.Tests;

/// <summary>The check for a newer PinList on GitHub.</summary>
public class UpdatesTests : StoreTestBase
{
    const string Repo = "https://github.com/FilipRaic/pin-list/";
    const string Downloads = Repo + "releases/download/v2.3.0/";

    static string Release(string tag = "v2.3.0", bool draft = false, bool prerelease = false,
                          string apk = Downloads + "PinList.apk", string apkName = "PinList.apk") => $$"""
        {
          "tag_name": "{{tag}}", "draft": {{(draft ? "true" : "false")}}, "prerelease": {{(prerelease ? "true" : "false")}},
          "html_url": "{{Repo}}releases/tag/v2.3.0",
          "assets": [
            { "name": "PinList.exe", "browser_download_url": "{{Downloads}}PinList.exe" },
            { "name": "PinList.zip", "browser_download_url": "{{Downloads}}PinList.zip" },
            { "name": "{{apkName}}", "browser_download_url": "{{apk}}" }
          ]
        }
        """;

    /// <summary>An update check whose GitHub answers from a list (null: nothing readable); counts the questions.</summary>
    sealed class Script
    {
        public readonly Queue<Func<UpdateInfo?>> Answers = new();
        public int Asked;
        public TaskCompletionSource? Hold;

        public async Task<UpdateInfo?> Latest(CancellationToken ct)
        {
            Asked++;
            if (Hold != null) await Hold.Task;
            return Answers.Count > 0 ? Answers.Dequeue()() : null;
        }
    }

    readonly Script _github = new();
    static UpdateInfo Info(string version) => new(Version.Parse(version), ExeUrl: Repo + "releases/download/v" + version + "/PinList.exe");
    Updates Make(string current = "2.2.0.0") => new(_github.Latest, Version.Parse(current));

    // ================= Reading GitHub's answer =================

    [Theory]
    [InlineData("v2.2.0", "2.2.0")]
    [InlineData(" V3.0.1 ", "3.0.1")]
    [InlineData("2.4", "2.4.0")]
    [InlineData("2.4.1.7", "2.4.1")]
    public void Version_tags_are_read(string tag, string version) =>
        Assert.Equal(Version.Parse(version), Updates.ParseVersion(tag));

    [Theory]
    [InlineData("nightly")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_tags_are_not_versions(string? tag) => Assert.Null(Updates.ParseVersion(tag));

    [Fact]
    public void A_release_gives_its_version_and_the_download_of_each_app()
    {
        var info = Updates.Parse(Release())!;
        Assert.Equal(new Version(2, 3, 0), info.Version);
        Assert.Equal(Downloads + "PinList.exe", info.ExeUrl);
        Assert.Equal(Downloads + "PinList.zip", info.MacUrl);
        Assert.Equal(Downloads + "PinList.apk", info.ApkUrl);
        Assert.Null(info.Notes); // no release notes
    }

    [Fact]
    public void Files_with_other_names_are_not_downloads()
    {
        var info = Updates.Parse(Release(apk: Downloads + "PinList-android.apk", apkName: "PinList-android.apk"))!;
        Assert.Null(info.ApkUrl);
    }

    [Theory]
    [InlineData("\"  ### Added\\n- New  \"", "### Added\n- New")]
    [InlineData("\"   \"", null)]
    [InlineData("7", null)]
    public void A_release_gives_its_notes(string body, string? notes) =>
        Assert.Equal(notes, Updates.Parse($$"""{ "tag_name": "v2.3.0", "body": {{body}} }""")!.Notes);

    [Fact]
    public void Links_outside_PinList_s_repository_are_not_used()
    {
        var info = Updates.Parse(Release(apk: "https://example.com/PinList.apk"))!;
        Assert.Null(info.ApkUrl);
        Assert.NotNull(info.ExeUrl);
    }

    [Theory]
    [InlineData("""{ "tag_name": "v2.3.0", "assets": [ 7, { "name": 3 } ] }""")]
    [InlineData("""{ "tag_name": "v2.3.0" }""")]
    public void A_release_without_files_has_no_downloads(string json)
    {
        var info = Updates.Parse(json)!;
        Assert.Null(info.ExeUrl);
        Assert.Null(info.MacUrl);
        Assert.Null(info.ApkUrl);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "message": "Not Found" }""")]
    [InlineData("""{ "tag_name": 230 }""")]
    [InlineData("""{ "tag_name": "nightly" }""")]
    public void Answers_that_are_not_a_release_are_ignored(string json) => Assert.Null(Updates.Parse(json));

    [Fact]
    public void Drafts_and_prereleases_are_ignored()
    {
        Assert.Null(Updates.Parse(Release(draft: true)));
        Assert.Null(Updates.Parse(Release(prerelease: true)));
    }

    [Fact]
    public async Task Asks_GitHub_s_API_for_the_latest_release()
    {
        var http = new FakeHttp();
        http.Answer(HttpStatusCode.OK, Release());
        var info = await Updates.Latest(new HttpClient(http), CancellationToken.None);

        Assert.Equal(new Version(2, 3, 0), info!.Version);
        Assert.Equal("https://api.github.com/repos/FilipRaic/pin-list/releases/latest", http.Url(0));
        var request = http.Requests[0].request;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("PinList", request.Headers.UserAgent.ToString());
        Assert.Contains("application/vnd.github+json", request.Headers.Accept.ToString());
        Assert.Null(request.Headers.Authorization); // nothing about the user is sent
    }

    [Fact]
    public async Task No_release_yet_is_no_update()
    {
        var http = new FakeHttp();
        http.Answer(HttpStatusCode.NotFound, """{ "message": "Not Found" }""");
        Assert.Null(await Updates.Latest(new HttpClient(http), CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.Forbidden)] // too many requests
    public async Task An_error_from_GitHub_is_a_failed_check(HttpStatusCode status)
    {
        var http = new FakeHttp();
        http.Answer(status, """{ "message": "Unavailable" }""");
        await Assert.ThrowsAsync<HttpRequestException>(() => Updates.Latest(new HttpClient(http), CancellationToken.None));
    }

    // ================= Checking =================

    [Fact]
    public async Task A_failed_check_is_tried_again_and_a_working_one_is_not()
    {
        Store.Data.Settings.CheckForUpdates = true;
        var updates = Make();
        var found = new List<UpdateInfo>();
        updates.Found += found.Add;

        await updates.RetryIfFailed(); // nothing failed yet: GitHub isn't asked
        Assert.Equal(0, _github.Asked);

        _github.Answers.Enqueue(() => throw new HttpRequestException("offline"));
        await updates.CheckNow();
        Assert.True(updates.LastCheckFailed);
        Assert.Empty(found);

        _github.Answers.Enqueue(() => Info("2.3.0")); // the network is back
        await updates.RetryIfFailed();
        Assert.False(updates.LastCheckFailed);
        Assert.Equal(new Version(2, 3, 0), Assert.Single(found).Version);

        await updates.RetryIfFailed(); // it worked: not asked again until the next daily check
        Assert.Equal(2, _github.Asked);
    }

    [Fact]
    public async Task Nothing_is_asked_until_the_user_said_yes()
    {
        var updates = Make();
        Assert.Null(updates.Enabled);
        await updates.CheckNow();
        updates.Enabled = false;
        await updates.CheckNow();
        Assert.Equal(0, _github.Asked);
        Assert.Null(updates.Available);
    }

    [Fact]
    public async Task A_newer_version_is_announced_once_and_stays_available()
    {
        Store.Data.Settings.CheckForUpdates = true;
        var updates = Make();
        var found = new List<UpdateInfo>();
        int changed = 0;
        updates.Found += found.Add;
        updates.Changed += () => changed++;

        _github.Answers.Enqueue(() => Info("2.3.0"));
        await updates.CheckNow();
        Assert.Equal(new Version(2, 3, 0), updates.Available!.Version);
        Assert.Equal(new Version(2, 3, 0), Assert.Single(found).Version);
        Assert.Equal("2.3.0", Store.Data.Settings.UpdateAnnounced);
        Assert.Equal(1, changed);
        Assert.True(Saver.Requests > 0);

        // the next check (or the next start) finds the same version: no second announcement
        _github.Answers.Enqueue(() => Info("2.3.0"));
        await Make().CheckNow();
        _github.Answers.Enqueue(() => Info("2.3.0"));
        await updates.CheckNow();
        Assert.Single(found);
        Assert.Equal(1, changed);
        Assert.NotNull(updates.Available);

        // an even newer one is announced again
        _github.Answers.Enqueue(() => Info("2.4.0"));
        await updates.CheckNow();
        Assert.Equal(2, found.Count);
        Assert.Equal(new Version(2, 4, 0), updates.Available!.Version);
    }

    [Theory]
    [InlineData("2.2.0")]
    [InlineData("2.1.9")]
    public async Task The_same_or_an_older_version_is_no_update(string latest)
    {
        Store.Data.Settings.CheckForUpdates = true;
        var updates = Make("2.2.0.0");
        updates.Found += _ => Assert.Fail("announced");
        _github.Answers.Enqueue(() => Info(latest));
        await updates.CheckNow();
        Assert.Null(updates.Available);
        Assert.Null(Store.Data.Settings.UpdateAnnounced);
    }

    [Fact]
    public async Task After_updating_the_update_is_gone()
    {
        Store.Data.Settings.CheckForUpdates = true;
        var old = Make("2.2.0");
        _github.Answers.Enqueue(() => Info("2.3.0"));
        await old.CheckNow();
        Assert.NotNull(old.Available);

        var updated = Make("2.3.0");
        _github.Answers.Enqueue(() => Info("2.3.0"));
        await updated.CheckNow();
        Assert.Null(updated.Available);
    }

    [Fact]
    public async Task A_failed_check_is_logged_and_tried_again_later()
    {
        Store.Data.Settings.CheckForUpdates = true;
        var updates = Make();
        _github.Answers.Enqueue(() => throw new HttpRequestException("No such host"));
        await updates.CheckNow(); // doesn't throw
        Assert.Null(updates.Available);
        Assert.Contains("Update check: No such host", File.ReadAllText(Path.Combine(Dir, "log.txt")));

        _github.Answers.Enqueue(() => Info("2.3.0"));
        await updates.CheckNow();
        Assert.NotNull(updates.Available);
    }

    [Fact]
    public async Task Turning_it_on_checks_right_away_and_off_forgets_the_update()
    {
        var updates = Make();
        int changed = 0;
        updates.Changed += () => changed++;
        _github.Answers.Enqueue(() => Info("2.3.0"));

        updates.Enabled = true;
        await Task.Yield();
        Assert.Equal(1, _github.Asked);
        Assert.NotNull(updates.Available);
        Assert.True(Store.Data.Settings.CheckForUpdates);

        updates.Enabled = true; // no change
        Assert.Equal(1, _github.Asked);

        updates.Enabled = false;
        Assert.Null(updates.Available);
        Assert.False(Store.Data.Settings.CheckForUpdates);
        Assert.Equal(3, changed); // on, found, off
    }

    [Fact]
    public async Task Turned_off_while_asking_means_no_update()
    {
        Store.Data.Settings.CheckForUpdates = true;
        var updates = Make();
        _github.Hold = new TaskCompletionSource();
        _github.Answers.Enqueue(() => Info("2.3.0"));
        var check = updates.CheckNow();
        await updates.CheckNow(); // one check at a time
        Assert.Equal(1, _github.Asked);

        updates.Enabled = false;
        _github.Hold.SetResult();
        await check;
        Assert.Null(updates.Available);
        Assert.Null(Store.Data.Settings.UpdateAnnounced);
    }

    [Fact]
    public void Checks_on_start_and_then_once_a_day()
    {
        Store.Data.Settings.CheckForUpdates = true;
        var updates = Make();
        updates.Start();
        Assert.Equal(1, _github.Asked);
        var timer = Assert.Single(ManualTimer.Created);
        Assert.Equal(TimeSpan.FromHours(24), timer.Interval);
        Assert.True(timer.IsEnabled);

        timer.Fire();
        Assert.Equal(2, _github.Asked);

        updates.Stop();
        Assert.False(timer.IsEnabled);
        updates.Start(); // the same timer again
        Assert.Single(ManualTimer.Created);
        Assert.True(timer.IsEnabled);
    }
}
