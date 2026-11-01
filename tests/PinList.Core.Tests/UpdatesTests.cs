using System.Net;
using PinList;
using PinList.Tests.Sync;
using Xunit;

namespace PinList.Tests;

/// <summary>The check for a newer PinList on GitHub.</summary>
public class UpdatesTests : StoreTestBase
{
    const string Repo = "https://github.com/FilipRaic/pin-list/";

    static string Release(string tag = "v2.3.0", bool draft = false, bool prerelease = false,
                          string page = Repo + "releases/tag/v2.3.0",
                          string apk = Repo + "releases/download/v2.3.0/PinList-android.apk") => $$"""
        {
          "tag_name": "{{tag}}", "draft": {{(draft ? "true" : "false")}}, "prerelease": {{(prerelease ? "true" : "false")}},
          "html_url": "{{page}}",
          "assets": [
            { "name": "PinList.exe", "browser_download_url": "{{Repo}}releases/download/v2.3.0/PinList.exe" },
            { "name": "PinList-android.apk", "browser_download_url": "{{apk}}" }
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
    static UpdateInfo Info(string version) => new(Version.Parse(version), Repo + "releases/tag/v" + version, null);
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
    public void A_release_gives_its_version_page_and_apk()
    {
        var info = Updates.Parse(Release())!;
        Assert.Equal(new Version(2, 3, 0), info.Version);
        Assert.Equal(Repo + "releases/tag/v2.3.0", info.PageUrl);
        Assert.Equal(Repo + "releases/download/v2.3.0/PinList-android.apk", info.ApkUrl);
    }

    [Fact]
    public void Links_outside_PinList_s_repository_are_not_used()
    {
        var info = Updates.Parse(Release(page: "https://example.com/pinlist", apk: "https://example.com/PinList-android.apk"))!;
        Assert.Equal(Updates.ReleasesPage, info.PageUrl);
        Assert.Null(info.ApkUrl);
    }

    [Fact]
    public void A_release_without_links_still_has_the_releases_page()
    {
        var info = Updates.Parse("""{ "tag_name": "v2.3.0", "assets": [ 7, { "name": 3 } ] }""")!;
        Assert.Equal(Updates.ReleasesPage, info.PageUrl);
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

    // ================= Checking =================

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
