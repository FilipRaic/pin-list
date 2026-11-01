using PinList.Sync;

namespace PinList.Tests.Sync;

/// <summary>
/// An in-memory cloud folder shared by several simulated devices. It can go offline and fail the next calls.
/// </summary>
sealed class FakeCloud : ICloudStore
{
    sealed record Entry(string Id, string Json, DateTime Modified);

    readonly Dictionary<string, Entry> _files = new();
    int _nextId;

    /// <summary>Time of the next write (every write moves it on by one second).</summary>
    public DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public bool Offline { get; set; }

    /// <summary>Like Drive with the user's access token: false means Google signed the user out.</summary>
    public Func<bool> Authorized = () => true;
    public readonly Queue<CloudError> FailNext = new();
    public int Calls, Uploads;

    /// <summary>Waits before every call until the test lets it go on (to test calls running at the same time).</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Runs at the start of every call (e.g. to change the app's state in the middle of a pass).</summary>
    public Action? OnCall { get; set; }

    async Task Check(CancellationToken ct)
    {
        Calls++;
        OnCall?.Invoke();
        if (Gate != null) await Gate.Task.WaitAsync(ct);
        if (Offline) throw new CloudException(CloudError.Offline, "offline");
        if (!Authorized()) throw new CloudException(CloudError.Unauthorized, "Not signed in.");
        if (FailNext.Count > 0) throw new CloudException(FailNext.Dequeue(), "failed");
    }

    public async Task<IReadOnlyList<CloudFile>> List(CancellationToken ct)
    {
        await Check(ct);
        return _files.Select(f => new CloudFile(f.Value.Id, f.Key, f.Value.Modified)).ToList();
    }

    public async Task<string> Download(CloudFile f, CancellationToken ct)
    {
        await Check(ct);
        var entry = _files.Values.FirstOrDefault(e => e.Id == f.Id) ?? throw new CloudException(CloudError.Other, "HTTP 404");
        return entry.Json;
    }

    public async Task Upload(string name, string json, CancellationToken ct)
    {
        await Check(ct);
        Uploads++;
        Now = Now.AddSeconds(1);
        _files[name] = new Entry(_files.TryGetValue(name, out var e) ? e.Id : $"id{++_nextId}", json, Now);
    }

    public async Task Delete(CloudFile f, CancellationToken ct)
    {
        await Check(ct);
        _files.Remove(f.Name);
    }

    // ---- For tests ----

    public IEnumerable<string> Names => _files.Keys.OrderBy(n => n, StringComparer.Ordinal);
    public bool Has(string name) => _files.ContainsKey(name);
    public string Read(string name) => _files[name].Json;
    public ListCopy Copy(string name) => ListCopy.Parse(Read(name));

    /// <summary>Writes a file as another device would.</summary>
    public void Put(string name, string json)
    {
        Now = Now.AddSeconds(1);
        _files[name] = new Entry(_files.TryGetValue(name, out var e) ? e.Id : $"id{++_nextId}", json, Now);
    }
}
