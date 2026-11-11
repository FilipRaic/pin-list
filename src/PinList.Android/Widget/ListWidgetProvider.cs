using System;
using System.Diagnostics.CodeAnalysis;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Views;
using Android.Widget;
using Uri = Android.Net.Uri;

namespace PinList.Android;

/// <summary>
/// The home screen widget: one list, chosen when the widget is placed. What it shows comes from
/// <see cref="WidgetModel"/>, this class only puts it into the views Android draws in the launcher.
/// PinList refreshes the widgets itself: after every save (also a sync's), after a tap and at midnight.
/// </summary>
[BroadcastReceiver(Label = "PinList", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData("android.appwidget.provider", Resource = "@xml/list_widget_info")]
[ExcludeFromCodeCoverage(Justification = "Runs inside Android's launcher widgets.")]
public class ListWidgetProvider : AppWidgetProvider
{
    /// <summary>How long the Undo bar shows after an item was completed on the widget.</summary>
    public const int UndoMs = 5000;

    public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? ids)
    {
        if (context == null || ids == null) return;
        foreach (int id in ids) Refresh(context, id); // the one that logs a failure: an exception here would end the app
        Alarms.ScheduleMidnight(context);
    }

    public override void OnDeleted(Context? context, int[]? ids)
    {
        if (context == null || ids == null) return;
        foreach (int id in ids) WidgetPrefs.Forget(context, id);
    }

    /// <summary>The first widget was placed: the due texts need a refresh when the day changes.</summary>
    public override void OnEnabled(Context? context)
    {
        if (context != null) Alarms.ScheduleMidnight(context);
    }

    /// <summary>The last widget of this size was removed (the alarm stays while one of the other size is left).</summary>
    public override void OnDisabled(Context? context)
    {
        if (context != null) Alarms.ScheduleMidnight(context);
    }

    /// <summary>The placed widgets of both sizes.</summary>
    static int[] Ids(Context context, AppWidgetManager manager)
    {
        var ids = new System.Collections.Generic.List<int>();
        foreach (var size in new[] { typeof(ListWidgetProvider), typeof(NarrowListWidgetProvider) })
            ids.AddRange(manager.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(size))) ?? Array.Empty<int>());
        return ids.ToArray();
    }

    public static bool AnyPlaced(Context context) =>
        AppWidgetManager.GetInstance(context) is { } manager && Ids(context, manager).Length > 0;

    /// <summary>Draws every placed widget again. Does nothing when there are none.</summary>
    public static void RefreshAll(Context context)
    {
        try
        {
            if (AppWidgetManager.GetInstance(context) is not { } manager) return;
            foreach (int id in Ids(context, manager)) Refresh(context, manager, id);
        }
        catch (Exception ex) { Store.Log("Widget: " + ex.Message); }
    }

    public static void Refresh(Context context, int widgetId)
    {
        try
        {
            if (AppWidgetManager.GetInstance(context) is { } manager) Refresh(context, manager, widgetId);
        }
        catch (Exception ex) { Store.Log("Widget: " + ex.Message); }
    }

    static void Refresh(Context context, AppWidgetManager manager, int widgetId)
    {
        string? boardId = WidgetPrefs.BoardId(context, widgetId);
        var state = WidgetData.State(context, boardId);
        var look = state.Look;
        bool deleted = state.Status == WidgetStatus.Deleted;
        var views = new RemoteViews(context.PackageName, Resource.Layout.list_widget);

        Tint(views, Resource.Id.widget_background, look.Background);

        // Header: title and buttons
        views.SetTextViewText(Resource.Id.widget_title, state.Title);
        views.SetTextColor(Resource.Id.widget_title, Color(look.Text));
        foreach (int button in new[] { Resource.Id.widget_add, Resource.Id.widget_refresh, Resource.Id.widget_open })
            Tint(views, button, look.Muted);
        views.SetViewVisibility(Resource.Id.widget_add, Shown(!deleted));
        views.SetViewVisibility(Resource.Id.widget_open, Shown(!deleted));
        views.SetViewVisibility(Resource.Id.widget_refresh, Shown(state.CanSync));
        // faded while a sync started with it is running (WidgetSync says "Synced" when it's done)
        if (WidgetSync.Running(context)) Tint(views, Resource.Id.widget_refresh, (look.Muted & 0x00FFFFFF) | 0x40000000);
        if (!deleted)
        {
            var open = OpenApp(context, widgetId, state.BoardId, null);
            views.SetOnClickPendingIntent(Resource.Id.widget_title, open);
            views.SetOnClickPendingIntent(Resource.Id.widget_open, open);
            // a list without categories has nowhere to add to: the app's list screen makes the first category
            views.SetOnClickPendingIntent(Resource.Id.widget_add,
                state.HasCategories ? AddBox(context, widgetId) : open);
            views.SetOnClickPendingIntent(Resource.Id.widget_refresh, Tap(context, widgetId, WidgetTapActivity.Sync, null));
        }

        // The list's rows come from ListWidgetService. Taps on rows are filled into this one template.
        var rows = new Intent(context, typeof(ListWidgetService)).PutExtra(AppWidgetManager.ExtraAppwidgetId, widgetId);
        rows.SetData(Uri.Parse(rows.ToUri(IntentUriType.Scheme))); // one adapter per widget
#pragma warning disable CA1422 // the newer way (RemoteCollectionItems) needs Android 12
        views.SetRemoteAdapter(Resource.Id.widget_list, rows);
#pragma warning restore CA1422
        views.SetPendingIntentTemplate(Resource.Id.widget_list, RowTemplate(context, widgetId));
        views.SetViewVisibility(Resource.Id.widget_list, Shown(state.Rows.Count > 0));

        // "No items yet", or "This list was deleted" with "Choose a list" (a widget not set up yet only has the button)
        bool message = state.Status == WidgetStatus.Empty || (deleted && boardId != null);
        views.SetViewVisibility(Resource.Id.widget_message, Shown(message));
        views.SetTextViewText(Resource.Id.widget_message, deleted ? WidgetModel.DeletedText : WidgetModel.EmptyText);
        views.SetTextColor(Resource.Id.widget_message, Color(look.Muted));
        views.SetViewVisibility(Resource.Id.widget_choose, Shown(deleted));
        views.SetTextViewText(Resource.Id.widget_choose, WidgetModel.ChooseText);
        views.SetTextColor(Resource.Id.widget_choose, Color(look.Accent));
        views.SetOnClickPendingIntent(Resource.Id.widget_choose, ChooseList(context, widgetId));

        // "Completed  Undo" for a few seconds after a completion on this widget
        var undo = WidgetPrefs.Undo(context, widgetId);
        bool undoShown = !deleted && undo is { } u && u.untilMs > Java.Lang.JavaSystem.CurrentTimeMillis();
        views.SetViewVisibility(Resource.Id.widget_undo_bar, Shown(undoShown));
        if (undoShown)
        {
            views.SetTextViewText(Resource.Id.widget_undo_text, WidgetModel.CompletedText);
            views.SetTextColor(Resource.Id.widget_undo_text, Color(look.Muted));
            views.SetTextViewText(Resource.Id.widget_undo, WidgetModel.UndoText);
            views.SetTextColor(Resource.Id.widget_undo, Color(look.Accent));
            views.SetOnClickPendingIntent(Resource.Id.widget_undo, Tap(context, widgetId, WidgetTapActivity.Undo, undo!.Value.itemId));
        }

        manager.UpdateAppWidget(widgetId, views);
#pragma warning disable CA1422 // see SetRemoteAdapter above
        manager.NotifyAppWidgetViewDataChanged(widgetId, Resource.Id.widget_list);
#pragma warning restore CA1422
    }

    // ---- Small helpers for the views ----

    internal static ViewStates Shown(bool shown) => shown ? ViewStates.Visible : ViewStates.Gone;

    internal static global::Android.Graphics.Color Color(uint argb) => new(unchecked((int)argb));

    /// <summary>Colors a white drawable. The color's transparency goes to the image, since a tint replaces the white fully.</summary>
    internal static void Tint(RemoteViews views, int imageId, uint argb)
    {
        views.SetInt(imageId, "setColorFilter", unchecked((int)(argb | 0xFF000000)));
        views.SetInt(imageId, "setImageAlpha", (int)(argb >> 24));
    }

    // ---- What taps do ----

    const PendingIntentFlags Fixed = PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent;

    /// <summary>Each widget and each kind of tap needs its own address, or Android would hand out one intent for all.</summary>
    static Uri Address(int widgetId, string what) => Uri.Parse($"pinlist://widget/{widgetId}/{what}")!;

    /// <summary>Opens PinList on the list, and on an item in it if given.</summary>
    internal static Intent OpenAppIntent(Context context, int widgetId, string boardId, string? itemId) =>
        new Intent(context, typeof(MainActivity))
            .SetData(Address(widgetId, "open"))!
            .AddFlags(ActivityFlags.NewTask)!
            .PutExtra(MainActivity.BoardExtra, boardId)!
            .PutExtra(MainActivity.ItemExtra, itemId)!;

    static PendingIntent OpenApp(Context context, int widgetId, string boardId, string? itemId) =>
        PendingIntent.GetActivity(context, 0, OpenAppIntent(context, widgetId, boardId, itemId), Fixed)!;

    static PendingIntent AddBox(Context context, int widgetId) =>
        PendingIntent.GetActivity(context, 0,
            new Intent(context, typeof(AddItemActivity))
                .SetData(Address(widgetId, "add"))!
                .AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask)!
                .PutExtra(AppWidgetManager.ExtraAppwidgetId, widgetId), Fixed)!;

    static PendingIntent ChooseList(Context context, int widgetId) =>
        PendingIntent.GetActivity(context, 0,
            new Intent(context, typeof(ChooseListActivity))
                .SetData(Address(widgetId, "choose"))!
                .AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask)!
                .PutExtra(AppWidgetManager.ExtraAppwidgetId, widgetId), Fixed)!;

    static Intent TapIntent(Context context, int widgetId, string what) =>
        new Intent(context, typeof(WidgetTapActivity))
            .SetData(Address(widgetId, what))!
            .AddFlags(ActivityFlags.NewTask | ActivityFlags.NoAnimation)!
            .PutExtra(AppWidgetManager.ExtraAppwidgetId, widgetId)!;

    /// <summary>A tap on one of the header's or the Undo bar's buttons.</summary>
    static PendingIntent Tap(Context context, int widgetId, string action, string? id) =>
        PendingIntent.GetActivity(context, 0,
            TapIntent(context, widgetId, action).PutExtra(WidgetTapActivity.ActionExtra, action)!.PutExtra(WidgetTapActivity.IdExtra, id), Fixed)!;

    /// <summary>A tap on a row: the row fills in what was tapped (see ListWidgetService), so this one may be changed.</summary>
    static PendingIntent RowTemplate(Context context, int widgetId) =>
        PendingIntent.GetActivity(context, 0, TapIntent(context, widgetId, "row"),
            PendingIntentFlags.UpdateCurrent | (OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0))!;
}

/// <summary>
/// The same widget, 2 cells wide when placed. Android lists each size as its own widget, so a second size needs a
/// class of its own (after placing, both resize freely).
/// </summary>
[BroadcastReceiver(Label = "PinList, narrow", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData("android.appwidget.provider", Resource = "@xml/list_widget_narrow_info")]
[ExcludeFromCodeCoverage(Justification = "Runs inside Android's launcher widgets.")]
public class NarrowListWidgetProvider : ListWidgetProvider
{
}
