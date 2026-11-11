using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Android.Content;
using Android.OS;
using PinList.Sync;

namespace PinList.Android;

/// <summary>Which list each home screen widget shows, and its small leftovers. Kept on this phone only.</summary>
[ExcludeFromCodeCoverage(Justification = "Android's SharedPreferences.")]
static class WidgetPrefs
{
    static ISharedPreferences Prefs(Context c) => c.GetSharedPreferences("widgets", FileCreationMode.Private)!;

    public static string? BoardId(Context c, int widgetId) => Prefs(c).GetString($"board.{widgetId}", null);

    public static void SetBoardId(Context c, int widgetId, string boardId) =>
        Prefs(c).Edit()!.PutString($"board.{widgetId}", boardId)!.Apply();

    /// <summary>The category the add box used last in this widget.</summary>
    public static string? LastCategory(Context c, int widgetId) => Prefs(c).GetString($"category.{widgetId}", null);

    public static void SetLastCategory(Context c, int widgetId, string categoryId) =>
        Prefs(c).Edit()!.PutString($"category.{widgetId}", categoryId)!.Apply();

    /// <summary>The item the widget's Undo bar brings back, and until when the bar shows (milliseconds since 1970).</summary>
    public static (string itemId, long untilMs)? Undo(Context c, int widgetId)
    {
        var id = Prefs(c).GetString($"undo.{widgetId}", null);
        return id == null ? null : (id, Prefs(c).GetLong($"undoUntil.{widgetId}", 0));
    }

    public static void SetUndo(Context c, int widgetId, string? itemId, long untilMs = 0)
    {
        var edit = Prefs(c).Edit()!;
        if (itemId == null) edit.Remove($"undo.{widgetId}")!.Remove($"undoUntil.{widgetId}");
        else edit.PutString($"undo.{widgetId}", itemId)!.PutLong($"undoUntil.{widgetId}", untilMs);
        edit.Apply();
    }

    /// <summary>The widget was removed from the home screen.</summary>
    public static void Forget(Context c, int widgetId) =>
        Prefs(c).Edit()!.Remove($"board.{widgetId}")!.Remove($"category.{widgetId}")!
            .Remove($"undo.{widgetId}")!.Remove($"undoUntil.{widgetId}")!.Apply();
}

/// <summary>
/// The widgets' way to the data. There is one copy of the data in the process, and the app's screen is bound to
/// it, so everything a widget reads or changes runs on the main thread: with the app's own data when its screen
/// exists, otherwise with data.json loaded for this (see <see cref="Alarms.EnsureData"/>).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Runs on Android's main thread.")]
static class WidgetData
{
    /// <summary>Without the app's screen there are no timers to save later with: a change is saved right after it.</summary>
    sealed class SaveAfter : ISaveScheduler
    {
        bool _pending;
        public void Schedule() => _pending = true;

        public bool CancelPending()
        {
            bool was = _pending;
            _pending = false;
            return was;
        }
    }

    static int _depth;

    /// <summary>Runs work on the data. With <paramref name="save"/>, what it changed is on disk when this returns.</summary>
    public static T Run<T>(Context context, bool save, Func<T> work)
    {
        if (Looper.MyLooper() == Looper.MainLooper) return OnMain(context, save, work);

        // e.g. the launcher asking for the list's rows: wait for the main thread to do it
        T result = default!;
        Exception? error = null;
        var done = new ManualResetEventSlim();
        new Handler(Looper.MainLooper!).Post(() =>
        {
            try { result = OnMain(context, save, work); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });
        if (!done.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The main thread is busy.");
        if (error != null) throw new InvalidOperationException(error.Message, error);
        return result;
    }

    static T OnMain<T>(Context context, bool save, Func<T> work)
    {
        bool live = Avalonia.Application.Current is PhoneApp { View: not null };
        // not when called from inside another Run (a save refreshes the widgets): that one loaded the data already
        if (!live && _depth == 0)
        {
            Alarms.EnsureData(context);
            Store.Saver = new SaveAfter();
            Actions.ClearUndo(); // the undo history belongs to the data that was just replaced
        }

        _depth++;
        try
        {
            var result = work();
            if (save) Store.Flush(); // Android may end the process right after a tap was handled
            return result;
        }
        finally { _depth--; }
    }

    /// <summary>What the widget of this list shows now.</summary>
    public static WidgetState State(Context context, string? boardId) =>
        Run(context, false, () => WidgetModel.For(Store.Data, boardId, SignedIn(context)));

    static bool SignedIn(Context context) =>
        SyncService.Current is { } sync
            ? sync.Account != null
            : PhoneApp.Auth != null && SyncState.Load(context.FilesDir!.AbsolutePath).Account != null;
}
