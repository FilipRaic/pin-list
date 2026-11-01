using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using PinList.Sync;

namespace PinList;

/// <summary>Decides when a pending save runs. The app waits until changes stop; tests replace it.</summary>
public interface ISaveScheduler
{
    /// <summary>A change happened; save soon.</summary>
    void Schedule();

    /// <summary>Cancels a pending save. Returns true if one was pending.</summary>
    bool CancelPending();
}

/// <summary>Saves 300 ms after the LAST change, on the UI thread.</summary>
public sealed class DebouncedSaveScheduler : ISaveScheduler
{
    readonly Action _save;
    IUiTimer? _timer;

    public DebouncedSaveScheduler(Action save) => _save = save;

    public void Schedule()
    {
        if (_timer == null)
        {
            _timer = UiTimers.Create();
            _timer.Interval = TimeSpan.FromMilliseconds(300);
            _timer.Tick += () => { _timer.Stop(); _save(); };
        }
        _timer.Stop(); // restart, so a burst of changes is saved once
        _timer.Start();
    }

    public bool CancelPending()
    {
        if (_timer?.IsEnabled != true) return false;
        _timer.Stop();
        return true;
    }
}

/// <summary>
/// Loads and saves everything to data.json in the data folder (%APPDATA%\PinList on Windows).
/// Every change calls MarkDirty(); the file is written 300 ms after the last change.
/// Writes are atomic (temp file + File.Replace), so a crash or power loss can't corrupt the existing file.
/// </summary>
public static class Store
{
    public static readonly string DefaultDir = DataFolderFor(OperatingSystem.IsMacOS());

    /// <summary>
    /// Where each system keeps app data: %APPDATA%\PinList on Windows, ~/Library/Application Support/PinList on
    /// macOS. (Android sets its own folder with UseDirectory.)
    /// </summary>
    public static string DataFolderFor(bool macOS) => macOS
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "PinList")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PinList");

    public static string Dir { get; private set; } = DefaultDir;

    static string FilePath => Path.Combine(Dir, "data.json");
    static string TempPath => Path.Combine(Dir, "data.json.tmp");
    public static string BackupDir => Path.Combine(Dir, "backups");
    static string LogPath => Path.Combine(Dir, "log.txt");

    const int BackupsToKeep = 7;
    const long MaxLogBytes = 1024 * 1024;

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new WallClockConverter() },
    };

    /// <summary>The JSON settings of data.json, also used for the cloud files.</summary>
    internal static JsonSerializerOptions JsonOptions => Options;

    public static AppData Data { get; private set; } = new();

    /// <summary>When saves run. Tests replace this with one that does nothing.</summary>
    public static ISaveScheduler Saver { get; set; } = new DebouncedSaveScheduler(SaveNow);

    /// <summary>
    /// Shows a message to the user (warning = false, error = true). Only logged until the app sets it to show
    /// a dialog at startup; tests replace it.
    /// </summary>
    public static Action<string, bool> Notify { get; set; } = (message, _) => Log(message);

    static int _failedSaves;

    /// <summary>Uses another data folder (tests, or the PINLIST_DATA_DIR variable while developing). Clears the data in memory.</summary>
    public static void UseDirectory(string dir)
    {
        Dir = dir;
        Data = new AppData();
        _failedSaves = 0;
    }

    /// <summary>Uses a folder and data that are already in memory (tests switch between simulated devices).</summary>
    internal static void Use(string dir, AppData data)
    {
        Dir = dir;
        Data = data;
        _failedSaves = 0;
    }

    /// <summary>data.json was written (sync uploads changes a few seconds later).</summary>
    public static event Action? Saved;

    // ================= Loading =================

    /// <summary>Returns true if existing data was loaded, false on first run.</summary>
    public static bool Load()
    {
        Directory.CreateDirectory(Dir);

        // If the machine died between writing the temp file and replacing, the temp file is the good copy.
        if (!File.Exists(FilePath) && File.Exists(TempPath))
        {
            try { File.Move(TempPath, FilePath); } catch { }
        }

        if (!File.Exists(FilePath)) return false;

        try
        {
            Data = ReadMainFile();
            DailyBackup();
            Loaded();
            return true;
        }
        catch (Exception ex)
        {
            Log("Invalid data.json: " + ex);
            try { File.Copy(FilePath, FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { }

            var latest = Directory.Exists(BackupDir)
                ? Directory.GetFiles(BackupDir, "data-*.json").OrderByDescending(f => f).FirstOrDefault()
                : null;
            if (latest != null)
            {
                try
                {
                    Data = ReadFile(latest, out _);
                    Loaded();
                    Notify($"The data file was damaged, so the latest backup was loaded:\n{Path.GetFileName(latest)}", false);
                    return true;
                }
                catch (Exception backupEx) { Log("Backup could not be read either: " + backupEx.Message); }
            }

            Notify("The data file is damaged and no valid backup exists. The damaged file was kept in:\n" + Dir, true);
            Data = new AppData();
            return true; // don't run first-run setup on top of damaged data
        }
    }

    /// <summary>
    /// After loading: a change that was saved but not yet stamped (the app was killed in between) gets its stamp now.
    /// </summary>
    static void Loaded()
    {
        SyncState.ReconcileCounter(Data, SyncState.Load(Dir)); // the counter never goes back, even after a restore
        ChangeTracker.StampChanges(Data, DateTime.UtcNow);
    }

    /// <summary>Reads data.json. A file from a newer PinList is loaded too, after a copy of it was kept.</summary>
    static AppData ReadMainFile()
    {
        var data = ReadFile(FilePath, out int fileVersion);
        if (fileVersion <= AppData.CurrentVersion) return data;

        var copy = Path.Combine(Dir, $"data.v{fileVersion}.json");
        try { if (!File.Exists(copy)) File.Copy(FilePath, copy); }
        catch (Exception ex) { Log("Could not keep a copy of the newer file: " + ex.Message); }

        Notify("Your lists were saved by a newer version of PinList. They were loaded, but details only the newer " +
               $"version knows about may be lost. A copy of the original file was kept as {Path.GetFileName(copy)}.", false);
        return data;
    }

    /// <summary>Reads a data file (also used for import). Throws if the file isn't valid PinList data.</summary>
    public static AppData ReadFile(string path, out int fileVersion) =>
        Parse(File.ReadAllText(path, Encoding.UTF8), out fileVersion);

    public static AppData Parse(string json, out int fileVersion)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Not a JSON object.");
        if (root["boards"] is not JsonArray) throw new InvalidDataException("This isn't a PinList data file.");

        fileVersion = root["version"]?.GetValue<int>() ?? AppData.CurrentVersion;
        var data = root.Deserialize<AppData>(Options) ?? new AppData();
        data.Settings ??= new Settings();
        data.Boards ??= new();
        data.Archive ??= new();
        foreach (var a in data.Archive)
            if (string.IsNullOrEmpty(a.Id)) a.Id = Guid.NewGuid().ToString("N");
        data.Version = AppData.CurrentVersion;
        return data;
    }

    /// <summary>The widget created on first run (top-right corner of the given work area).</summary>
    public static Board DefaultBoard(WorkArea workArea, string style = "Dark")
    {
        var b = new Board { Title = "My list", Width = 320, Height = 480, Style = style };
        b.X = workArea.Right - b.Width - 24;
        b.Y = workArea.Top + 24;
        b.Categories.Add(new Category { Name = "General", Color = Actions.Palette[0] });
        return b;
    }

    // ================= Saving =================

    public static void MarkDirty() => Saver.Schedule();

    /// <summary>Saves immediately if there is a pending change (exit, sign-out, shutdown, export).</summary>
    public static void Flush()
    {
        if (Saver.CancelPending()) SaveNow();
    }

    public static void SaveNow()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            ChangeTracker.StampChanges(Data, DateTime.UtcNow);
            WriteAtomic(FilePath, Data);
            _failedSaves = 0;
            DailyBackup();
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            // e.g. antivirus or OneDrive briefly holding the file – try again
            Log("Save failed: " + ex.Message);
            if (++_failedSaves <= 5) MarkDirty();
        }
    }

    /// <summary>The JSON written to data.json and export files (see docs/data-format.html).</summary>
    public static string Serialize(AppData data) => JsonSerializer.Serialize(data, Options);

    static void WriteAtomic(string path, AppData data) => WriteTextAtomic(path, Serialize(data));

    /// <summary>Writes a file so that a crash leaves either the old or the new content, never a mix.</summary>
    internal static void WriteTextAtomic(string path, string text)
    {
        var temp = path + ".tmp";
        var bytes = Encoding.UTF8.GetBytes(text);
        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true); // really on disk, not just in the OS cache
        }

        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
    }

    /// <summary>
    /// One copy of data.json per day, the last 7 are kept. Runs on start and after saving,
    /// so the backups stay current even if PinList runs for weeks without a restart.
    /// </summary>
    /// <summary>Called before a sync merge removes anything: today's backup must exist first (spec 6.2).</summary>
    public static void BackupBeforeSync() => DailyBackup();

    static void DailyBackup()
    {
        try
        {
            var today = Path.Combine(BackupDir, $"data-{DateTime.Now:yyyy-MM-dd}.json");
            if (File.Exists(today) || !File.Exists(FilePath)) return;

            Directory.CreateDirectory(BackupDir);
            File.Copy(FilePath, today);
            foreach (var old in Directory.GetFiles(BackupDir, "data-*.json").OrderByDescending(f => f).Skip(BackupsToKeep))
                File.Delete(old);
        }
        catch (Exception ex) { Log("Backup: " + ex.Message); }
    }

    // ================= Export and import =================

    public static void Export(string path) => WriteTextAtomic(path, ExportJson());

    /// <summary>
    /// The export file: all data, but without this device's sync identity and with every list's sync turned off,
    /// so importing it somewhere never makes a list sync by itself (spec 5.8).
    /// </summary>
    public static string ExportJson()
    {
        Flush();
        var copy = Parse(Serialize(Data), out _);
        foreach (var b in copy.Boards)
        {
            b.Synced = false;
            b.SyncedChanged = null;
        }
        copy.SyncInfo = null;
        return Serialize(copy);
    }

    /// <summary>True if any list syncs (import then leaves those alone).</summary>
    public static bool HasSyncedBoards => Data.Boards.Any(b => b.Synced);

    /// <summary>
    /// Replaces all data (import). A copy of the current data is kept in the backups folder first. Synced lists and
    /// their archive stay: replacing them would delete them on the user's other devices too (spec 5.8).
    /// </summary>
    public static string ReplaceAll(AppData imported)
    {
        Flush();
        Directory.CreateDirectory(BackupDir);
        var safety = Path.Combine(BackupDir, $"before-import-{DateTime.Now:yyyy-MM-dd-HHmmss}.json");
        WriteAtomic(safety, Data);

        var synced = Data.Boards.Where(b => b.Synced).ToList();
        var syncedIds = synced.Select(b => b.Id).ToHashSet();
        foreach (var b in imported.Boards.Where(b => syncedIds.Contains(b.Id)).ToList()) imported.Boards.Remove(b);
        foreach (var b in imported.Boards)
        {
            // a raw backup may still say "synced": an import never turns sync on (spec 5.8)
            b.Synced = false;
            b.SyncedChanged = null;
        }
        for (int i = 0; i < synced.Count; i++) imported.Boards.Insert(i, synced[i]);
        var archive = imported.Archive.Where(a => !syncedIds.Contains(a.BoardId))
            .Concat(Data.Archive.Where(a => syncedIds.Contains(a.BoardId)))
            .OrderByDescending(a => a.CompletedAt).ToList();
        imported.Archive = new(archive);

        imported.Settings = Data.Settings; // the theme is a setting of this PC, not of the lists
        imported.SyncInfo = Data.SyncInfo; // still the same device
        Data = imported;
        SaveNow();
        return safety;
    }

    /// <summary>Adds imported windows and archive items next to the existing ones (with new ids, so nothing clashes).</summary>
    public static IReadOnlyList<Board> Merge(AppData imported)
    {
        ReassignIds(imported);
        ForgetSync(imported);
        foreach (var b in imported.Boards) Data.Boards.Add(b);
        foreach (var a in imported.Archive) Data.Archive.Add(a);
        var archive = Data.Archive.OrderByDescending(a => a.CompletedAt).ToList();
        Data.Archive.Clear();
        foreach (var a in archive) Data.Archive.Add(a);
        MarkDirty();
        return imported.Boards.ToList();
    }

    /// <summary>Imported lists are new local lists here: no sync flag, no stamps (they're stamped as new).</summary>
    static void ForgetSync(AppData data)
    {
        foreach (var b in data.Boards)
        {
            (b.Synced, b.Sync, b.OrderSync, b.SyncedChanged, b.Seen) = (false, null, null, null, null);
            foreach (var c in b.Categories)
            {
                (c.Sync, c.OrderSync) = (null, null);
                foreach (var i in c.Items) i.Sync = null;
            }
        }
        foreach (var a in data.Archive) a.Sync = null;
    }

    /// <summary>New ids for every window, category and item; archive entries follow their category and window.</summary>
    public static void ReassignIds(AppData data)
    {
        var map = new Dictionary<string, string>();
        string NewId(string old)
        {
            var id = Guid.NewGuid().ToString("N");
            if (old.Length > 0) map[old] = id;
            return id;
        }

        foreach (var b in data.Boards)
        {
            b.Id = NewId(b.Id);
            foreach (var c in b.Categories)
            {
                c.Id = NewId(c.Id);
                foreach (var i in c.Items) i.Id = NewId(i.Id);
            }
        }
        foreach (var a in data.Archive)
        {
            // an archive entry whose category isn't in the file must not point to an existing category on this PC
            a.CategoryId = map.TryGetValue(a.CategoryId, out var c) ? c : "";
            a.BoardId = map.TryGetValue(a.BoardId, out var b) ? b : "";
        }
    }

    // ================= Log =================

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            // Keep the log small: when it passes 1 MB, the old part moves to log.old.txt (replacing the previous one)
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > MaxLogBytes)
                File.Move(LogPath, Path.Combine(Dir, "log.old.txt"), true);

            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    // ================= Lookups =================

    public static IEnumerable<TodoItem> AllItems() =>
        Data.Boards.SelectMany(b => b.Categories).SelectMany(c => c.Items);

    public static (Board board, Category category)? Locate(TodoItem item)
    {
        foreach (var b in Data.Boards)
            foreach (var c in b.Categories)
                if (c.Items.Contains(item)) return (b, c);
        return null;
    }

    public static Board? BoardOf(Category category) =>
        Data.Boards.FirstOrDefault(b => b.Categories.Contains(category));

    public static Category? FindCategory(string id) =>
        Data.Boards.SelectMany(b => b.Categories).FirstOrDefault(c => c.Id == id);

    public static Category? FirstCategory() =>
        Data.Boards.SelectMany(b => b.Categories).FirstOrDefault();
}
