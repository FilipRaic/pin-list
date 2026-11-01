using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PinList;

/// <summary>A newer PinList on GitHub: its version, its release page and (for the phone) its APK.</summary>
public sealed record UpdateInfo(Version Version, string PageUrl, string? ApkUrl);

/// <summary>
/// Looks for a newer PinList about once a day, while the user has said yes to that (Settings.CheckForUpdates).
/// It only asks GitHub for the latest release of PinList: no data and no id of the user is sent.
/// <see cref="Found"/> announces each new version once; <see cref="Available"/> stays set for the menus.
/// </summary>
public sealed class Updates
{
    public const string ReleasesPage = "https://github.com/FilipRaic/pin-list/releases/latest";
    const string LatestReleaseApi = "https://api.github.com/repos/FilipRaic/pin-list/releases/latest";
    const string RepoPrefix = "https://github.com/FilipRaic/pin-list/";
    const string ApkName = "PinList-android.apk";

    /// <summary>How often PinList looks again while it keeps running.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    readonly Func<CancellationToken, Task<UpdateInfo?>> _latest;
    IUiTimer? _timer;
    bool _checking;

    /// <param name="latest">Gets the latest release (null when it can't be read).</param>
    /// <param name="current">The version of this app.</param>
    public Updates(Func<CancellationToken, Task<UpdateInfo?>> latest, Version current)
    {
        _latest = latest;
        Current = Plain(current);
    }

    /// <summary>The real check, against PinList's releases on GitHub.</summary>
    public static Updates ForGitHub(HttpClient http, Version current) => new(ct => Latest(http, ct), current);

    /// <summary>This app's version (major.minor.patch).</summary>
    public Version Current { get; }

    /// <summary>A newer version than this one, once a check found it.</summary>
    public UpdateInfo? Available { get; private set; }

    /// <summary><see cref="Available"/> or the setting changed (the menus follow).</summary>
    public event Action? Changed;

    /// <summary>A newer version was found that wasn't announced before (each version once).</summary>
    public event Action<UpdateInfo>? Found;

    /// <summary>True: checks. False: doesn't. Null: the user wasn't asked yet.</summary>
    public bool? Enabled
    {
        get => Store.Data.Settings.CheckForUpdates;
        set
        {
            if (Store.Data.Settings.CheckForUpdates == value) return;
            Store.Data.Settings.CheckForUpdates = value;
            Store.MarkDirty();
            if (value != true) Available = null;
            Changed?.Invoke();
            if (value == true) _ = CheckNow();
        }
    }

    /// <summary>Checks now (if allowed) and then once every <see cref="Interval"/>.</summary>
    public void Start()
    {
        if (_timer == null)
        {
            _timer = UiTimers.Create();
            _timer.Interval = Interval;
            _timer.Tick += () => _ = CheckNow();
        }
        _timer.Start();
        _ = CheckNow();
    }

    public void Stop() => _timer?.Stop();

    /// <summary>Asks GitHub for the latest release, if the user allowed it. Never throws (a failed check is logged).</summary>
    public async Task CheckNow()
    {
        if (Enabled != true || _checking) return;
        _checking = true;
        try
        {
            UpdateInfo? latest;
            try { latest = await _latest(CancellationToken.None); }
            catch (Exception ex)
            {
                Store.Log("Update check: " + ex.Message); // offline, GitHub down, ... try again next time
                return;
            }
            if (Enabled != true) return; // turned off while it was asking

            var newer = latest != null && latest.Version > Current ? latest : null;
            if (newer != Available)
            {
                Available = newer;
                Changed?.Invoke();
            }
            if (newer == null || Store.Data.Settings.UpdateAnnounced == newer.Version.ToString()) return;
            Store.Data.Settings.UpdateAnnounced = newer.Version.ToString();
            Store.MarkDirty();
            Found?.Invoke(newer);
        }
        finally { _checking = false; }
    }

    /// <summary>
    /// The latest release from GitHub's API, or null if the answer isn't a release with a version tag.
    /// Throws when GitHub can't be reached.
    /// </summary>
    internal static async Task<UpdateInfo?> Latest(HttpClient http, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.ParseAdd("PinList"); // GitHub's API refuses requests without one
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return Parse(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Reads GitHub's answer about a release. Links that don't point into PinList's repository are ignored.</summary>
    internal static UpdateInfo? Parse(string json)
    {
        JsonNode? release;
        try { release = JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException) { return null; }
        if (release is not JsonObject r || ParseVersion(Text(r["tag_name"])) is not { } version) return null;
        if (r["draft"]?.GetValueKind() == System.Text.Json.JsonValueKind.True
            || r["prerelease"]?.GetValueKind() == System.Text.Json.JsonValueKind.True) return null;

        var page = Text(r["html_url"]) is { } p && p.StartsWith(RepoPrefix, StringComparison.Ordinal) ? p : ReleasesPage;
        var apk = (r["assets"] as JsonArray)?.OfType<JsonObject>()
            .Where(a => Text(a["name"]) == ApkName)
            .Select(a => Text(a["browser_download_url"]))
            .FirstOrDefault(u => u != null && u.StartsWith(RepoPrefix, StringComparison.Ordinal));
        return new UpdateInfo(version, page, apk);
    }

    /// <summary>"v2.2.0" or "2.2.0" as a version; null for anything else.</summary>
    internal static Version? ParseVersion(string? tag)
    {
        var s = (tag ?? "").Trim().TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? Plain(v) : null;
    }

    static string? Text(JsonNode? node) =>
        node?.GetValueKind() == System.Text.Json.JsonValueKind.String ? node.GetValue<string>() : null;

    /// <summary>Major.minor.patch only, so 2.2.0 and the assembly's 2.2.0.0 are the same version.</summary>
    static Version Plain(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
