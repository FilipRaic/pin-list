using System;
using System.Diagnostics.CodeAnalysis;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Widget;
using PinList.Sync;

namespace PinList.Android;

/// <summary>
/// The widget's sync button. A widget can't show progress, so a tap says "Syncing" in a short message over the
/// home screen and fades the button, and the end of that sync says how it went ("Synced", or what's wrong).
/// Without a network it says so right away, since the sync would only start later.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Android toasts and network state.")]
static class WidgetSync
{
    /// <summary>The button stops looking busy after this, also when the sync never reported back.</summary>
    const int TimeoutMs = 30000;

    const string Key = "syncAskedUntil";

    static ISharedPreferences Prefs(Context c) => c.GetSharedPreferences("widgets", FileCreationMode.Private)!;

    /// <summary>A sync started from a widget is running.</summary>
    public static bool Running(Context context) =>
        Prefs(context).GetLong(Key, 0) > Java.Lang.JavaSystem.CurrentTimeMillis();

    /// <summary>The sync button was tapped.</summary>
    public static void Start(Context context)
    {
        if (!Online(context))
        {
            Say(context, WidgetModel.OfflineText);
            BackgroundSync.SyncSoon(context); // it runs when there's a network again
            return;
        }

        Prefs(context).Edit()!.PutLong(Key, Java.Lang.JavaSystem.CurrentTimeMillis() + TimeoutMs)!.Apply();
        Say(context, WidgetModel.SyncingText);
        BackgroundSync.SyncSoon(context);
        ListWidgetProvider.RefreshAll(context);
        new Handler(Looper.MainLooper!).PostDelayed(() => ListWidgetProvider.RefreshAll(context), TimeoutMs + 200);
    }

    /// <summary>A background sync ended. If a widget's button started it, say how it went.</summary>
    public static void Done(Context context, SyncStatus? status)
    {
        if (!Running(context)) return;
        Prefs(context).Edit()!.Remove(Key)!.Apply();
        Say(context, status switch
        {
            null or SyncStatus.Waiting => WidgetModel.SyncWaitingText,
            SyncStatus.Problem => WidgetModel.SyncProblemText,
            _ => WidgetModel.SyncedText,
        });
        ListWidgetProvider.RefreshAll(context);
    }

    static bool Online(Context context)
    {
        try { return (context.GetSystemService(Context.ConnectivityService) as ConnectivityManager)?.ActiveNetwork != null; }
        catch (Exception) { return true; } // can't tell: let the sync find out
    }

    /// <summary>A short message over the home screen (a toast), from any thread.</summary>
    static void Say(Context context, string text) =>
        new Handler(Looper.MainLooper!).Post(() =>
        {
            try { Toast.MakeText(context, text, ToastLength.Short)!.Show(); }
            catch (Exception ex) { Store.Log("Widget message: " + ex.Message); }
        });
}
