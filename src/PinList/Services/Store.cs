using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace PinList;

/// <summary>
/// Loads and saves everything to %APPDATA%\PinList\data.json.
/// Every change calls MarkDirty(); the file is written 300 ms after the last change.
/// Writes are atomic (temp file + File.Replace), so a crash or power loss can't corrupt the existing file.
/// </summary>
public static class Store
{
    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PinList");

    static string FilePath => Path.Combine(Dir, "data.json");
    static string TempPath => Path.Combine(Dir, "data.json.tmp");
    static string BackupDir => Path.Combine(Dir, "backups");
    const int BackupsToKeep = 7;

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppData Data { get; private set; } = new();

    static DispatcherTimer? _timer;
    static int _failedSaves;

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
            Data = Read(FilePath);
            DailyBackup();
            return true;
        }
        catch (Exception ex)
        {
            Log("Neispravan data.json: " + ex);
            try { File.Copy(FilePath, FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { }

            var latest = Directory.Exists(BackupDir)
                ? Directory.GetFiles(BackupDir, "data-*.json").OrderByDescending(f => f).FirstOrDefault()
                : null;
            if (latest != null)
            {
                try
                {
                    Data = Read(latest);
                    MessageBox.Show($"The data file was damaged, so the latest backup was loaded:\n{Path.GetFileName(latest)}",
                        "PinList", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return true;
                }
                catch { }
            }

            MessageBox.Show("The data file is damaged and no valid backup exists. The damaged file was kept in:\n" + Dir,
                "PinList", MessageBoxButton.OK, MessageBoxImage.Error);
            Data = new AppData();
            return true; // don't run first-run setup on top of damaged data
        }
    }

    static AppData Read(string path) =>
        JsonSerializer.Deserialize<AppData>(File.ReadAllText(path, Encoding.UTF8), Options) ?? new AppData();

    /// <summary>The widget created on first run (top-right corner of the primary screen).</summary>
    public static Board DefaultBoard()
    {
        var wa = SystemParameters.WorkArea;
        var b = new Board { Title = "My list", Width = 320, Height = 480, Style = "Dark" };
        b.X = wa.Right - b.Width - 24;
        b.Y = wa.Top + 24;
        b.Categories.Add(new Category { Name = "General", Color = Actions.Palette[0] });
        return b;
    }

    public static void MarkDirty()
    {
        if (_timer == null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _timer.Tick += OnTick;
        }
        _timer.Stop(); // restart: save 300 ms after the LAST change
        _timer.Start();
    }

    static void OnTick(object? sender, EventArgs e)
    {
        _timer!.Stop();
        SaveNow();
    }

    /// <summary>Saves immediately if there is a pending change (exit, sign-out, shutdown).</summary>
    public static void Flush()
    {
        if (_timer?.IsEnabled == true)
        {
            _timer.Stop();
            SaveNow();
        }
    }

    public static void SaveNow()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Data, Options));
            using (var fs = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true); // really on disk, not just in the OS cache
            }

            if (File.Exists(FilePath)) File.Replace(TempPath, FilePath, null);
            else File.Move(TempPath, FilePath);

            _failedSaves = 0;
        }
        catch (Exception ex)
        {
            // e.g. antivirus or OneDrive briefly holding the file – try again
            Log("Spremanje nije uspjelo: " + ex.Message);
            if (++_failedSaves <= 5) MarkDirty();
        }
    }

    static void DailyBackup()
    {
        try
        {
            Directory.CreateDirectory(BackupDir);
            var today = Path.Combine(BackupDir, $"data-{DateTime.Now:yyyy-MM-dd}.json");
            if (!File.Exists(today)) File.Copy(FilePath, today);

            foreach (var old in Directory.GetFiles(BackupDir, "data-*.json").OrderByDescending(f => f).Skip(BackupsToKeep))
                File.Delete(old);
        }
        catch (Exception ex) { Log("Backup: " + ex.Message); }
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Path.Combine(Dir, "log.txt"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    // ---- Lookups ----

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
