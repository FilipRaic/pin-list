using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PinList.Sync;

/// <summary>
/// Runs sync (spec 5–6 with 9): timers, sign-in, the per-list on/off switch, and sync passes that merge other
/// devices' files into the local lists and upload this device's own files. All work happens on the caller's (UI)
/// thread; only network calls are awaited. One pass at a time, foreground or background (Android).
/// </summary>
public sealed class SyncService
{
    /// <summary>The app's sync service (null until the app creates it, and on platforms without sync).</summary>
    public static SyncService? Current { get; set; }

    /// <summary>One pass at a time in the whole process, whether the app or a background job started it.</summary>
    static readonly SemaphoreSlim Gate = new(1, 1);

    static readonly TimeSpan[] Backoff =
        { TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5) };

    const string ResetFile = "reset.json";
    const string Unreachable = "Can't reach Google Drive.";
    const string SignedOut = "Google signed you out.";
    const string NewerFormat = "Update PinList to keep syncing this window.";

    readonly IGoogleAuth _auth;
    readonly Func<ICloudStore> _cloudFactory;
    ICloudStore? _cloud;
    readonly string _deviceName;
    readonly Func<DateTime> _utcNow;
    readonly string _dir;
    SyncState? _state;

    bool _syncing, _waiting, _needsSignIn;
    int _failures;
    string? _problem;
    readonly Dictionary<string, string> _listProblems = new();
    IUiTimer? _every, _upload, _retry;

    public SyncService(IGoogleAuth auth, Func<ICloudStore> cloud, string deviceName, Func<DateTime>? utcNow = null)
    {
        _auth = auth;
        _cloudFactory = cloud;
        _deviceName = deviceName;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _dir = Store.Dir;
    }

    SyncState State => _state ??= SyncState.Load(_dir);

    // ================= What the UI shows =================

    public bool IsAvailable => _auth.IsAvailable;
    public string UnavailableText => _auth.UnavailableText;

    /// <summary>The signed-in account, or null.</summary>
    public GoogleAccount? Account => IsAvailable ? State.Account : null;

    public DateTime? LastSyncUtc => State.LastSyncUtc;
    public bool IsSyncing => _syncing;

    /// <summary>Google needs the user to sign in again (access was removed).</summary>
    public bool NeedsSignIn => _needsSignIn;

    /// <summary>When the next try after a failure runs (null when the last pass worked).</summary>
    public TimeSpan? RetryIn { get; private set; }

    /// <summary>The account or any list's state changed.</summary>
    public event Action Changed = delegate { };

    /// <summary>A one-off message for the user (spec 5.6, 5.7).</summary>
    public event Action<string> Message = delegate { };

    /// <summary>Where lists from other devices open (the desktop app sets the primary screen's work area).</summary>
    public Func<WorkArea> WorkArea { get; set; } = () => new WorkArea(0, 0, 1280, 800);

    /// <summary>
    /// True while the user edits or drags that board, category or item: remote changes to its list wait until the
    /// edit ends, so typing is never overwritten. The UI may replace it (e.g. to add drags).
    /// </summary>
    public Func<object, bool> IsBusy { get; set; } = IsEditing;

    /// <summary>An edit box of this board, category or item is open.</summary>
    public static bool IsEditing(object o) =>
        o is TodoItem { IsEditing: true } or Category { IsEditing: true } or Board { IsEditingTitle: true };

    public ListSyncState StateOf(Board b)
    {
        if (Account == null) return new(SyncStatus.Hidden, null, null);
        var last = State.LastSyncUtc;
        if (_listProblems.TryGetValue(b.Id, out var problem)) return new(SyncStatus.Problem, last, problem);
        if (!b.Synced) return new(State.TurningOff.Contains(b.Id) ? SyncStatus.Waiting : SyncStatus.Local, last, null);
        if (_problem != null) return new(SyncStatus.Problem, last, _problem);
        if (_syncing) return new(SyncStatus.Syncing, last, null);
        if (_waiting) return new(SyncStatus.Waiting, last, null);
        return new(SyncStatus.UpToDate, last, null);
    }

    SyncStatus Overall =>
        Account == null ? SyncStatus.Hidden : _problem != null ? SyncStatus.Problem : _waiting ? SyncStatus.Waiting : SyncStatus.UpToDate;

    /// <summary>Signed in and at least one list syncs (Android then schedules background sync).</summary>
    public bool NeedsBackgroundSync => Account != null && Store.Data.Boards.Any(b => b.Synced);

    /// <summary>A synced list has local changes that aren't uploaded yet.</summary>
    public bool HasPendingUploads
    {
        get
        {
            if (Account == null || Store.Data.SyncInfo is not { } device) return false;
            var s = State;
            if (s.TurningOff.Count > 0 || s.Stripping.Count > 0 || s.Markers.Count > 0) return true;
            return Store.Data.Boards.Any(b => b.Synced && !_listProblems.ContainsKey(b.Id) &&
                s.Uploaded.GetValueOrDefault(FileName(b.Id, device.DeviceId)) != Hash(ListCopy.From(b, Store.Data.Archive, device, _deviceName)));
        }
    }

    void Raise() => Changed();

    // ================= Starting and stopping =================

    /// <summary>Syncs every 20 seconds and a few seconds after a save, while the app runs.</summary>
    public void Start()
    {
        _every = Timer(TimeSpan.FromSeconds(20), repeat: true, () => RetryIn == null); // after a failure, the retry decides
        _upload = Timer(TimeSpan.FromSeconds(5), repeat: false);
        _retry = Timer(Backoff[0], repeat: false);
        _every.Start();
        Store.Saved += OnSaved;
        Actions.BoardDeleted += OnBoardDeleted;
    }

    public void Stop()
    {
        foreach (var t in new[] { _every, _upload, _retry }) t?.Stop();
        Store.Saved -= OnSaved;
        Actions.BoardDeleted -= OnBoardDeleted;
    }

    IUiTimer Timer(TimeSpan interval, bool repeat, Func<bool>? when = null)
    {
        var t = UiTimers.Create();
        t.Interval = interval;
        t.Tick += () =>
        {
            if (!repeat) t.Stop();
            if (when?.Invoke() != false) _ = SyncNow();
        };
        return t;
    }

    void OnSaved()
    {
        if (!HasPendingUploads) return;
        _upload!.Stop(); // restart: uploads 5 s after the last save
        _upload.Start();
    }

    /// <summary>A synced list deleted here: its deleted marker goes to the cloud (also later, if signed out now).</summary>
    void OnBoardDeleted(Board board, bool byUser)
    {
        if (!byUser || !(board.Synced || State.TurningOff.Contains(board.Id))) return;
        var data = Store.Data;
        ChangeTracker.StampChanges(data, _utcNow());
        var marker = ListCopy.From(board, data.Archive, data.SyncInfo!, _deviceName).ToDeletedMarker(data.SyncInfo!, _utcNow());
        State.Markers[FileName(board.Id, data.SyncInfo!.DeviceId)] = ListCopy.ToJson(marker);
        State.DeletedLists.Add(board.Id);
        State.TurningOff.Remove(board.Id);
        SaveState();
        Store.MarkDirty();
        OnSaved();
    }

    // ================= Account =================

    /// <summary>Interactive sign-in. Signing in doesn't turn sync on for any list (spec 4.2).</summary>
    public async Task<bool> SignIn(CancellationToken ct)
    {
        if (!IsAvailable) return false;
        GoogleAccount? account;
        try { account = await _auth.SignIn(ct); }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            // never silent: a failed sign-in looks like "nothing happened" otherwise
            Store.Log("Google sign-in failed: " + ex);
            Message(ex is CloudException { Kind: CloudError.Offline }
                ? "Can't sign in: no connection to Google."
                : $"Google sign-in didn't work: {ex.Message.TrimEnd('.')}.");
            return false;
        }
        if (account == null) return false;

        var s = State;
        if (s.LastUserId != null && s.LastUserId != account.UserId)
        {
            // Another account: this device's synced lists become local here only (spec 5.7)
            foreach (var b in Store.Data.Boards) b.Synced = false;
            s.MergedFiles.Clear(); s.Uploaded.Clear(); s.Markers.Clear(); s.TurningOff.Clear(); s.Stripping.Clear();
            s.DeletedLists.Clear();
            s.LastSyncUtc = null;
            Store.MarkDirty();
        }
        s.Account = account;
        s.LastUserId = account.UserId;
        _needsSignIn = false;
        _problem = null;
        _failures = 0;
        SaveState();
        Raise();
        await SyncNow();
        return true;
    }

    /// <summary>
    /// Signs out: syncs first, then forgets the sign-in; lists stay and synced lists pause (spec 5.7). With
    /// <paramref name="deleteCloudData"/>, all of PinList's cloud files are deleted (other devices make their lists
    /// local when they see the reset marker) and this device's lists become local.
    /// </summary>
    public async Task SignOut(bool deleteCloudData)
    {
        if (State.Account == null) return;
        var s = State;
        if (deleteCloudData)
        {
            try
            {
                var cloud = Cloud;
                var me = Store.Data.SyncInfo?.DeviceId ?? "";
                // the marker first, so a device that syncs meanwhile sees it instead of an empty folder
                await cloud.Upload(ResetFile, new JsonObject { ["device"] = me, ["at"] = _utcNow().ToString("o") }.ToJsonString(), CancellationToken.None);
                foreach (var f in await cloud.List(CancellationToken.None))
                    if (f.Name != ResetFile) await cloud.Delete(f, CancellationToken.None);
            }
            catch (CloudException ex)
            {
                Store.Log("Sync: deleting the cloud data failed: " + ex.Message);
                Message(Unreachable + " The cloud data wasn't deleted.");
                return;
            }
            foreach (var b in Store.Data.Boards) b.Synced = false;
            s.MergedFiles.Clear(); s.Uploaded.Clear(); s.Markers.Clear(); s.TurningOff.Clear(); s.Stripping.Clear();
            Store.MarkDirty();
        }
        else
        {
            await RunOnce(CancellationToken.None);
            // a list turned off just before: its content leaves the cloud now, not at some later sign-in
            if (s.Stripping.Count > 0) await RunOnce(CancellationToken.None);
        }

        await _auth.SignOut();
        s.Account = null;
        _problem = null;
        _waiting = false;
        _needsSignIn = false;
        _listProblems.Clear();
        SaveState();
        Raise();
    }

    // ================= The per-list switch =================

    /// <summary>Turns sync on or off for a list (spec 4.3, 5.4, 5.5). Turning on while signed out signs in first.</summary>
    public async Task SetSynced(Board b, bool on)
    {
        if (!IsAvailable) return;
        if (on)
        {
            if (b.Synced) return;
            if (Account == null && !await SignIn(CancellationToken.None)) return;
            State.TurningOff.Remove(b.Id);
            State.Stripping.Remove(b.Id);
            b.Synced = true;
        }
        else
        {
            if (!b.Synced) return;
            b.Synced = false;
            State.TurningOff.Add(b.Id); // its last edits and the flag still have to go out (spec 5.5)
        }
        SaveState();
        Store.MarkDirty();
        Raise();
        await SyncNow();
    }

    // ================= Passes =================

    public async Task SyncNow() => await RunOnce(CancellationToken.None);

    /// <summary>One quick pass when the app exits; never waits longer than <paramref name="limit"/>.</summary>
    public async Task SyncBeforeExit(TimeSpan limit)
    {
        using var cts = new CancellationTokenSource();
        var pass = RunOnce(cts.Token);
        await Task.WhenAny(pass, Task.Delay(limit));
        // Cancel here, not with a second timer: a token source disposed before its timer fires never cancels,
        // and the pass would then wait for the network forever and hold the one-pass-at-a-time lock
        cts.Cancel();
    }

    /// <summary>The background job's entry point (Android). Uses the running app's data when there is one.</summary>
    public static async Task<HeadlessResult> RunHeadless(string dataDir, IGoogleAuth auth, Func<ICloudStore> cloud,
        string deviceName, Func<Func<Task<bool>>, Task<bool>>? onUiThread, CancellationToken ct)
    {
        if (onUiThread != null)
        {
            // The app runs: sync its live data on its thread (or nothing, if this build has no sync)
            if (Current is not { } live) return new HeadlessResult(false, SyncStatus.Hidden);
            var merged = await onUiThread(async () =>
            {
                var c = await live.RunOnce(ct);
                Store.Flush(); // Android may freeze or end the process right after the job
                return c;
            });
            return new HeadlessResult(merged, live.Overall);
        }

        // A process started only for the job: no UI, so no timers; everything is saved at the end
        Store.Saver = new SaveAtEnd();
        Store.UseDirectory(dataDir);
        Store.Load();
        var loaded = Store.Data;
        var service = new SyncService(auth, cloud, deviceName);
        var changed = await service.RunOnce(ct);
        if (ReferenceEquals(Store.Data, loaded)) Store.SaveNow();
        else
        {
            // The app opened meanwhile and loaded data.json itself: this pass's merges aren't in what it shows.
            // Forget which files were merged, so the app's own next pass merges them into its data.
            var state = SyncState.Load(dataDir);
            state.MergedFiles.Clear();
            state.Save(dataDir);
        }
        return new HeadlessResult(changed, service.Overall);
    }

    /// <summary>One full sync pass. Returns true if local data changed.</summary>
    public async Task<bool> RunOnce(CancellationToken ct)
    {
        if (Account == null) return false;
        await Gate.WaitAsync(CancellationToken.None); // a pass that already runs finishes or fails on its own

        bool changed = false;
        try
        {
            _syncing = true;
            Raise();
            await Pass(ct, () => changed = true);
            _failures = 0;
            _waiting = false;
            _problem = null;
            _needsSignIn = false;
            RetryIn = null;
            _retry?.Stop();
        }
        catch (CloudException ex) { Failed(ex); }
        catch (OperationCanceledException) { }
        finally
        {
            _syncing = false;
            Gate.Release(); // first: nothing below may keep the lock
            try { if (changed) Store.MarkDirty(); }
            finally { Raise(); }
        }
        return changed;
    }

    /// <summary>Saves nothing on its own: a background run saves once when it's done.</summary>
    sealed class SaveAtEnd : ISaveScheduler
    {
        public void Schedule() { }
        public bool CancelPending() => false;
    }

    void Failed(CloudException ex)
    {
        Store.Log("Sync: " + ex.Message);
        if (ex.Kind == CloudError.Unauthorized)
        {
            _needsSignIn = true;
            _problem = SignedOut;
            return;
        }
        _failures++;
        _waiting = true;
        if (_failures >= Backoff.Length) _problem = Unreachable;
        RetryIn = Backoff[Math.Min(_failures, Backoff.Length) - 1];
        if (_retry != null)
        {
            _retry.Stop();
            _retry.Interval = RetryIn.Value;
            _retry.Start();
        }
    }

    ICloudStore Cloud => _cloud ??= _cloudFactory();

    async Task Pass(CancellationToken ct, Action markChanged)
    {
        var data = Store.Data;
        var s = State;
        SyncState.ReconcileCounter(data, s);
        ChangeTracker.StampChanges(data, _utcNow());
        var me = data.SyncInfo!.DeviceId;
        var cloud = Cloud;
        var files = (await cloud.List(ct)).ToList();
        StillCurrent(data);
        _listProblems.Clear();

        // ---- Cloud data deleted from a device (spec 5.7) ----
        var reset = files.FirstOrDefault(f => f.Name == ResetFile);
        if (reset != null && !Merged(reset))
        {
            var by = Read(await cloud.Download(reset, ct), "device");
            if (by != me)
            {
                if (data.Boards.Any(b => b.Synced))
                {
                    foreach (var b in data.Boards) b.Synced = false;
                    markChanged();
                    Message("Sync was turned off because the cloud data was deleted from another device.");
                }
                // this device's files that were uploaded while the cloud data was being deleted go too
                foreach (var own in files.Where(f => ParseName(f.Name, out _, out var d) && d == me).ToList())
                {
                    await cloud.Delete(own, ct);
                    files.Remove(own);
                }
            }
            s.Uploaded.Clear(); s.UploadedSeen.Clear(); s.Markers.Clear(); s.TurningOff.Clear(); s.Stripping.Clear();
            s.MergedFiles[reset.Name] = reset.ModifiedUtc;
        }

        // ---- Copies to merge: other devices' new files, and this device's own file if the list here went back ----
        var toRead = new List<(CloudFile file, string boardId)>();
        foreach (var f in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            if (!ParseName(f.Name, out var boardId, out var device)) continue;
            if (device == me ? OwnCopyIsAhead(data, boardId, f.Name) : !Merged(f)) toRead.Add((f, boardId));
        }

        bool merged = false;
        foreach (var list in toRead.GroupBy(x => x.boardId))
        {
            var copies = new List<(CloudFile file, ListCopy copy)>();
            foreach (var (f, boardId) in list)
            {
                string json = await cloud.Download(f, ct);
                StillCurrent(data);
                ListCopy copy;
                try { copy = ListCopy.Parse(json); }
                catch (JsonException ex)
                {
                    Store.Log($"Sync: {f.Name} can't be read: {ex.Message}");
                    s.MergedFiles[f.Name] = f.ModifiedUtc;
                    continue;
                }
                if (copy.Format > ListCopy.CurrentFormat)
                {
                    _listProblems[boardId] = NewerFormat; // never merged, never overwritten
                    continue;
                }
                // this device's own stamps the cloud has seen: the counter never goes below them
                if (copy.Seen.TryGetValue(me, out var seenOfMine) && seenOfMine > data.SyncInfo!.Counter)
                    data.SyncInfo.Counter = seenOfMine;
                copies.Add((f, copy));
            }
            if (copies.Count == 0) continue;

            if (data.Boards.All(b => b.Id != list.Key))
            {
                // Not here: all copies decide together whether it appears, so an old copy from a device that was
                // offline can't bring back a list that was deleted (spec 5.3)
                if (Safely(list.Key, () => NewList(list.Key, copies.Select(c => c.copy).ToList())) == true)
                {
                    markChanged();
                    merged = true;
                }
                foreach (var (f, _) in copies) s.MergedFiles[f.Name] = f.ModifiedUtc;
                continue;
            }
            foreach (var (f, copy) in copies)
            {
                switch (Safely(f.Name, () => MergeFile(copy)))
                {
                    case null: continue; // being edited: try again next time
                    case true: markChanged(); merged = true; break;
                }
                s.MergedFiles[f.Name] = f.ModifiedUtc;
            }
        }

        StillCurrent(data);
        if (RemoveDuplicates(data, IsBusy)) { markChanged(); merged = true; }
        // what was merged is on disk before the files count as merged (the app may be ended any moment)
        if (merged) Store.SaveNow();

        // ---- This device's copies ----
        ChangeTracker.StampChanges(data, _utcNow());
        SaveState(); // the counter is safe on disk before any stamp leaves the device
        var present = files.Select(f => f.Name).ToHashSet();
        foreach (var b in data.Boards.ToList())
        {
            var name = FileName(b.Id, me);
            if (_listProblems.ContainsKey(b.Id)) continue;
            var copy = ListCopy.From(b, data.Archive, data.SyncInfo, _deviceName);
            if (s.Stripping.Contains(b.Id))
            {
                await Upload(name, copy.ToMarker(), present, ct);
                s.Stripping.Remove(b.Id);
            }
            else if (s.TurningOff.Contains(b.Id))
            {
                await Upload(name, copy, present, ct); // the last edits together with "sync off" (spec 5.5)
                s.TurningOff.Remove(b.Id);
                s.Stripping.Add(b.Id); // the content leaves the cloud on the next pass
            }
            else if (b.Synced) await Upload(name, copy, present, ct);
        }
        foreach (var (name, json) in s.Markers.ToList())
        {
            await Upload(name, ListCopy.Parse(json), present, ct);
            s.Markers.Remove(name);
        }

        s.LastSyncUtc = _utcNow();
        SaveState();
    }

    /// <summary>
    /// A pass works on the data it started with. If the app loaded data.json meanwhile (Android: the screen opened
    /// during a background pass), the pass stops; the app's own pass does the work on the data it shows.
    /// </summary>
    static void StillCurrent(AppData data)
    {
        if (!ReferenceEquals(Store.Data, data)) throw new OperationCanceledException("The data was loaded again.");
    }

    async Task Upload(string name, ListCopy copy, HashSet<string> present, CancellationToken ct)
    {
        var hash = Hash(copy);
        if (present.Contains(name) && State.Uploaded.GetValueOrDefault(name) == hash)
        {
            State.UploadedSeen.TryAdd(name, copy.Seen.GetValueOrDefault(copy.Device)); // the cloud has this copy
            return;
        }
        copy.UpdatedAt = _utcNow();
        await Cloud.Upload(name, ListCopy.ToJson(copy), ct);
        State.Uploaded[name] = hash;
        State.UploadedSeen[name] = copy.Seen.GetValueOrDefault(copy.Device);
        SaveState();
    }

    /// <summary>
    /// This device's own cloud copy has changes the list here doesn't (data.json was restored from a backup, or
    /// sync-state.json was lost): it is merged like another device's copy, instead of being overwritten.
    /// </summary>
    bool OwnCopyIsAhead(AppData data, string boardId, string name)
    {
        var local = data.Boards.FirstOrDefault(b => b.Id == boardId);
        if (local == null) return false;
        if (!State.UploadedSeen.TryGetValue(name, out var uploaded)) return true; // unknown: read it to be safe
        return (local.Seen?.GetValueOrDefault(data.SyncInfo!.DeviceId) ?? 0) < uploaded;
    }

    /// <summary>A list this device doesn't have: it appears if its copies together say it syncs and isn't deleted.</summary>
    bool NewList(string id, List<ListCopy> copies)
    {
        var s = State;
        if (copies.Any(c => c.Deleted)) s.DeletedLists.Add(id);
        if (s.DeletedLists.Contains(id)) return false;
        var combined = copies[0];
        foreach (var next in copies.Skip(1)) combined = SyncMerge.Merge(combined, next, out _);
        if (combined.IsMarker || !combined.Synced) return false;

        var created = new Board { Id = id };
        combined.ApplyTo(created, Store.Data.Archive);
        Actions.AddSyncedBoard(created, WorkArea());
        Store.Log($"Sync added list {id} from {combined.DeviceName}.");
        return true;
    }

    /// <summary>Merges another device's copy into the local list. Null when the list is busy; true if anything changed.</summary>
    bool? MergeFile(ListCopy theirs)
    {
        var data = Store.Data;
        var s = State;
        var id = theirs.BoardId;
        var local = data.Boards.FirstOrDefault(b => b.Id == id);

        if (local == null) return false; // removed by an earlier copy in this pass
        if (s.TurningOff.Contains(id)) return false; // the off switch is the last change for this list (spec 5.5)
        if (Busy(local)) return null;

        var mine = ListCopy.From(local, data.Archive, data.SyncInfo!, _deviceName);
        var result = SyncMerge.Merge(mine, theirs, out var report);

        // A local list starts syncing again only when it was turned on elsewhere later (spec 5.4)
        if (!local.Synced && (!result.Synced || result.Deleted)) return false;

        if (result.Deleted)
        {
            if (report.KeptLocalAfterRemoteDelete)
            {
                // Edited here after the delete: kept as a new local list (spec 5.6)
                var newId = Guid.NewGuid().ToString("N");
                foreach (var a in data.Archive.Where(a => a.BoardId == id)) a.BoardId = newId;
                local.Id = newId;
                local.Synced = false;
                local.SyncedChanged = null;
                local.Seen = null;
                s.DeletedLists.Add(id);
                s.Markers[FileName(id, data.SyncInfo!.DeviceId)] = ListCopy.ToJson(result);
                Store.Log($"Sync: list {id} was deleted on {theirs.DeviceName}; kept here as local list {newId}.");
                Message($"“{local.Title}” was deleted on another device. Your recent changes on this device were kept in a local window.");
                return true;
            }
            Store.BackupBeforeSync();
            Actions.RemoveSyncedBoard(local);
            s.DeletedLists.Add(id);
            s.Markers[FileName(id, data.SyncInfo!.DeviceId)] = ListCopy.ToJson(result); // only the marker stays in the cloud
            Store.Log($"Sync removed list {id}: deleted on {theirs.DeviceName}.");
            return true;
        }

        if (report.Removed.Count > 0) Store.BackupBeforeSync();
        foreach (var r in report.Removed) Store.Log($"Sync removed {r} from list {id}: removed on {theirs.DeviceName}.");
        foreach (var o in report.Overridden) Store.Log($"Sync took {theirs.DeviceName}'s newer version of {o} in list {id}.");
        bool wasSynced = local.Synced;
        bool changed = result.ApplyTo(local, data.Archive);
        if (wasSynced && !local.Synced)
        {
            // Turned off on another device: stop without sending anything but the marker (spec 5.5)
            s.Stripping.Add(id);
            Store.Log($"Sync: list {id} was turned off on {theirs.DeviceName}.");
        }
        return changed;
    }

    /// <summary>
    /// Runs a merge; a cloud file whose content doesn't fit together (written by a broken or changed PinList) is
    /// logged and skipped instead of stopping sync for every list. Returns false then.
    /// </summary>
    static bool? Safely(string what, Func<bool?> merge)
    {
        try { return merge(); }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or NullReferenceException or ArgumentException)
        {
            Store.Log($"Sync: {what} can't be merged: {ex.GetType().Name}");
            return false;
        }
    }

    bool Busy(Board b) =>
        IsBusy(b) || b.Categories.Any(c => IsBusy(c) || c.Items.Any(i => IsBusy(i)));

    /// <summary>
    /// Rule 8: an id lives in one place only. When merges put a category or an item into two places (moved on one
    /// device, edited on another), the newest copy stays. Ties are decided the same way on every device; copies that
    /// are being edited or dragged wait for the next pass.
    /// </summary>
    internal static bool RemoveDuplicates(AppData data, Func<object, bool> busy)
    {
        bool removed = false;
        var cats = data.Boards.SelectMany(b => b.Categories.Select(c => (owner: b.Id, list: b.Categories, c)))
            .GroupBy(x => x.c.Id).Where(g => g.Count() > 1).ToList();
        foreach (var g in cats)
        {
            if (g.Any(x => busy(x.c) || x.c.Items.Any(busy))) continue;
            var ordered = Newest(g, x => x.c.Sync, x => x.owner).ToList();
            // the copy that stays is a new change here, with its items: other devices must not take them for deleted
            var kept = ordered[0].c;
            foreach (var x in ordered.Skip(1))
            {
                x.list.Remove(x.c);
                // items only the other copy has move over: removing a copy never removes an item with it
                foreach (var item in x.c.Items.Where(i => kept.Items.All(k => k.Id != i.Id)).ToList())
                {
                    x.c.Items.Remove(item);
                    kept.Items.Add(item);
                }
                Store.Log($"Sync removed a second copy of category {x.c.Id}.");
                removed = true;
            }
            ChangeTracker.Touch(kept.Sync);
            ChangeTracker.Touch(kept.OrderSync);
            foreach (var i in kept.Items) ChangeTracker.Touch(i.Sync);
        }
        var items = data.Boards.SelectMany(b => b.Categories).SelectMany(c => c.Items.Select(i => (owner: c.Id, list: c.Items, i)))
            .GroupBy(x => x.i.Id).Where(g => g.Count() > 1).ToList();
        foreach (var g in items)
        {
            if (g.Any(x => busy(x.i))) continue;
            var ordered = Newest(g, x => x.i.Sync, x => x.owner).ToList();
            ChangeTracker.Touch(ordered[0].i.Sync);
            foreach (var x in ordered.Skip(1))
            {
                x.list.Remove(x.i);
                Store.Log($"Sync removed a second copy of {x.i.Id}.");
                removed = true;
            }
        }
        return removed;
    }

    /// <summary>Newest change first; then by stamp and by where it is, so every device picks the same copy.</summary>
    static IEnumerable<T> Newest<T>(IEnumerable<T> copies, Func<T, SyncMeta?> meta, Func<T, string> place) =>
        copies.OrderByDescending(x => meta(x)?.ModifiedAt ?? DateTime.MinValue)
            .ThenByDescending(x => meta(x)?.Changed?.Device ?? "", StringComparer.Ordinal)
            .ThenByDescending(x => meta(x)?.Changed?.Counter ?? 0)
            .ThenBy(place, StringComparer.Ordinal);

    bool Merged(CloudFile f) => State.MergedFiles.TryGetValue(f.Name, out var t) && t == f.ModifiedUtc;

    void SaveState()
    {
        if (Store.Data.SyncInfo is { } d) State.Counter = Math.Max(State.Counter, d.Counter);
        State.Save(_dir);
    }

    static string FileName(string boardId, string device) => $"w-{boardId}.{device}.json";

    /// <summary>"w-&lt;board&gt;.&lt;device&gt;.json" → board and device.</summary>
    static bool ParseName(string name, out string board, out string device)
    {
        board = device = "";
        if (!name.StartsWith("w-", StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal)) return false;
        var middle = name[2..^5];
        int dot = middle.IndexOf('.');
        if (dot <= 0) return false;
        (board, device) = (middle[..dot], middle[(dot + 1)..]);
        return true;
    }

    static string? Read(string json, string property)
    {
        try { return (string?)JsonNode.Parse(json)?[property]; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>Fingerprint of a copy without its upload time: unchanged lists aren't uploaded again.</summary>
    static string Hash(ListCopy copy)
    {
        var json = ListCopy.ToJson(copy.WithUpdatedAt(default));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
