using System;
using System.Diagnostics.CodeAnalysis;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;

namespace PinList.Android;

/// <summary>
/// A tap on a home screen widget lands here: complete, undo, collapse, sync now, or open the item in PinList.
/// It shows nothing and closes at once. It is a screen and not a broadcast receiver, because only a screen may
/// open PinList from a tap on newer Android versions.
/// </summary>
[Activity(Theme = "@style/PinListInvisibleTheme", Exported = false, NoHistory = true, ExcludeFromRecents = true,
    TaskAffinity = "")]
[ExcludeFromCodeCoverage(Justification = "Started by taps on Android's launcher widgets.")]
public class WidgetTapActivity : Activity
{
    public const string ActionExtra = "pinlist.action", IdExtra = "pinlist.id";
    public const string Complete = "complete", Undo = "undo", Collapse = "collapse", Sync = "sync", Open = "open";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        try { HandleTap(); }
        catch (Exception ex) { Store.Log("Widget tap: " + ex.Message); }
        Finish();
    }

    void HandleTap()
    {
        int widgetId = Intent?.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId)
                       ?? AppWidgetManager.InvalidAppwidgetId;
        string? action = Intent?.GetStringExtra(ActionExtra), id = Intent?.GetStringExtra(IdExtra);
        string? boardId = WidgetPrefs.BoardId(this, widgetId);
        if (widgetId == AppWidgetManager.InvalidAppwidgetId || action == null || boardId == null) return;

        var context = ApplicationContext!;
        switch (action)
        {
            case Complete when id != null:
                if (WidgetData.Run(context, true, () => WidgetActions.Complete(id)))
                {
                    WidgetPrefs.SetUndo(context, widgetId, id, Java.Lang.JavaSystem.CurrentTimeMillis() + ListWidgetProvider.UndoMs);
                    // take the Undo bar away again when its time is over
                    new Handler(Looper.MainLooper!).PostDelayed(() => ListWidgetProvider.Refresh(context, widgetId),
                        ListWidgetProvider.UndoMs + 200);
                }
                else WidgetPrefs.SetUndo(context, widgetId, null);
                Changed(context, widgetId, boardId);
                break;

            case Undo when id != null:
                // a bar that outlived its time (Android froze the app before it was taken away) only goes away
                if (WidgetPrefs.Undo(context, widgetId) is { } bar && bar.itemId == id
                    && bar.untilMs > Java.Lang.JavaSystem.CurrentTimeMillis())
                    WidgetData.Run(context, true, () => WidgetActions.UndoComplete(id));
                WidgetPrefs.SetUndo(context, widgetId, null);
                Changed(context, widgetId, boardId);
                break;

            case Collapse when id != null:
                WidgetData.Run(context, true, () => WidgetActions.ToggleCollapsed(id));
                WidgetPrefs.SetUndo(context, widgetId, null);
                Changed(context, widgetId, boardId);
                break;

            case Sync:
                WidgetPrefs.SetUndo(context, widgetId, null);
                WidgetSync.Start(context); // says "Syncing", and "Synced" when it's done
                ListWidgetProvider.Refresh(context, widgetId);
                break;

            case Open:
                StartActivity(ListWidgetProvider.OpenAppIntent(this, widgetId, boardId, id));
                break;
        }
    }

    /// <summary>
    /// After a change made on a widget: the widget shows it, reminders are planned again (a completed item's
    /// reminder is gone), and a synced list goes to the other devices now instead of at the next background sync.
    /// </summary>
    internal static void Changed(Context context, int widgetId, string boardId)
    {
        ListWidgetProvider.Refresh(context, widgetId);
        Alarms.ScheduleNext(context);
        if (WidgetData.State(context, boardId).CanSync) BackgroundSync.SyncSoon(context);
    }
}
