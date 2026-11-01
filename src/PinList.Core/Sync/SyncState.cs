using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PinList.Sync;

/// <summary>
/// sync-state.json next to data.json: the signed-in account, a copy of this device's change counter, which cloud
/// files were merged, lists that are being turned off and the last successful sync. Not exported, not backed up.
/// Losing it is harmless: every cloud file is read again and the stamps make that safe.
/// </summary>
public sealed class SyncState
{
    public const string FileName = "sync-state.json";

    public GoogleAccount? Account { get; set; }

    /// <summary>The account of the last sign-in (kept after signing out, to tell the same account from another).</summary>
    public string? LastUserId { get; set; }

    /// <summary>This device's id (the same as data.json's syncInfo.deviceId).</summary>
    public string? DeviceId { get; set; }

    /// <summary>Copy of the change counter: it must never go back, even if data.json is restored from a backup.</summary>
    public long Counter { get; set; }

    /// <summary>Cloud file name → its modified time when it was last merged.</summary>
    public Dictionary<string, DateTime> MergedFiles { get; set; } = new();

    /// <summary>Lists whose "sync off" still has to be uploaded (spec 5.5: waiting while offline).</summary>
    public HashSet<string> TurningOff { get; set; } = new();

    /// <summary>Lists whose own cloud copy becomes a marker on the next pass (after "sync off").</summary>
    public HashSet<string> Stripping { get; set; } = new();

    /// <summary>Lists deleted on this device: they never come back from other devices' older files.</summary>
    public HashSet<string> DeletedLists { get; set; } = new();

    /// <summary>Own file name → fingerprint of what was uploaded last (unchanged copies aren't uploaded again).</summary>
    public Dictionary<string, string> Uploaded { get; set; } = new();

    /// <summary>Own file name → this device's own counter in the seen-list of what was uploaded last.</summary>
    public Dictionary<string, long> UploadedSeen { get; set; } = new();

    /// <summary>Markers still to be uploaded (file name → content), e.g. for a list deleted while offline.</summary>
    public Dictionary<string, string> Markers { get; set; } = new();

    public DateTime? LastSyncUtc { get; set; }

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new UtcConverter() },
    };

    public static SyncState Load(string dir)
    {
        var path = Path.Combine(dir, FileName);
        if (!File.Exists(path)) return new SyncState();
        try
        {
            var s = JsonSerializer.Deserialize<SyncState>(File.ReadAllText(path), Options) ?? new SyncState();
            s.MergedFiles ??= new();
            s.TurningOff ??= new();
            s.Stripping ??= new();
            s.DeletedLists ??= new();
            s.Uploaded ??= new();
            s.UploadedSeen ??= new();
            s.Markers ??= new();
            return s;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Store.Log($"{FileName} can't be read, sync starts over: {ex.Message}");
            return new SyncState();
        }
    }

    /// <summary>Writes the file atomically (like data.json).</summary>
    public void Save(string dir)
    {
        Directory.CreateDirectory(dir);
        Store.WriteTextAtomic(Path.Combine(dir, FileName), JsonSerializer.Serialize(this, Options));
    }

    /// <summary>
    /// Makes data.json and the state agree on this device's id and counter. The counter becomes the higher of both,
    /// so no stamp is ever used twice. Data that came from another device (a different id) gets a new id here.
    /// </summary>
    public static void ReconcileCounter(AppData data, SyncState state)
    {
        if (state.DeviceId == null)
        {
            if (data.SyncInfo == null) return;
            state.DeviceId = data.SyncInfo.DeviceId;
        }
        else if (data.SyncInfo == null)
            data.SyncInfo = new DeviceInfo { DeviceId = state.DeviceId, Counter = state.Counter };
        else if (data.SyncInfo.DeviceId != state.DeviceId)
        {
            var id = Guid.NewGuid().ToString("N");
            data.SyncInfo = new DeviceInfo { DeviceId = id };
            state.DeviceId = id;
            state.Counter = 0;
        }

        var counter = Math.Max(data.SyncInfo.Counter, state.Counter);
        data.SyncInfo.Counter = counter;
        state.Counter = counter;
    }
}
