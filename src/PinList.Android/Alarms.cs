using System;
using System.Linq;
using Android.App;
using Android.Content;
using Android.OS;

namespace PinList.Android;

/// <summary>
/// Wakes PinList for the next reminder, also when the app isn't running: one exact alarm, always for the
/// earliest reminder that hasn't gone off. When it rings, the receiver shows what's due and sets the next one.
/// </summary>
static class Alarms
{
    /// <summary>
    /// Loads the data for a receiver, unless the app has it already. Android also starts the app (and so loads
    /// the data) for a broadcast, and sends the boot broadcast late, e.g. on the first start after a force stop:
    /// loading again then would leave the screen working on a copy that is no longer saved.
    /// </summary>
    public static void EnsureData(Context context)
    {
        if (Avalonia.Application.Current is PhoneApp { View: not null }) return;
        Store.UseDirectory(context.FilesDir!.AbsolutePath);
        Store.Load();
    }

    public static void ScheduleNext(Context context)
    {
        try
        {
            var manager = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
            var intent = PendingIntent.GetBroadcast(context, 0, new Intent(context, typeof(ReminderReceiver)),
                PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
            var next = Store.AllItems()
                .Where(i => i.ReminderAt != null && !i.ReminderShown)
                .Select(i => i.ReminderAt!.Value)
                .DefaultIfEmpty(DateTime.MaxValue)
                .Min();
            manager.Cancel(intent);
            if (next == DateTime.MaxValue) return;

            long at = new DateTimeOffset(next).ToUnixTimeMilliseconds(); // wall-clock time on this phone
            if (!OperatingSystem.IsAndroidVersionAtLeast(31) || manager.CanScheduleExactAlarms())
                manager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, at, intent);
            else
                manager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at, intent);
        }
        catch (Exception ex) { Store.Log("Alarm: " + ex.Message); }
    }

    static PendingIntent MidnightIntent(Context context) =>
        PendingIntent.GetBroadcast(context, 1, new Intent(context, typeof(MidnightReceiver)),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

    /// <summary>
    /// Home screen widgets say "Today" and "3 days overdue", so they are drawn again when the day changes. The
    /// alarm doesn't need to be exact, and only exists while a widget is placed.
    /// </summary>
    public static void ScheduleMidnight(Context context)
    {
        try
        {
            var manager = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
            var intent = MidnightIntent(context);
            manager.Cancel(intent);
            if (!ListWidgetProvider.AnyPlaced(context)) return;
            long at = new DateTimeOffset(DateTime.Today.AddDays(1).AddSeconds(5)).ToUnixTimeMilliseconds();
            manager.SetAndAllowWhileIdle(AlarmType.Rtc, at, intent);
        }
        catch (Exception ex) { Store.Log("Midnight alarm: " + ex.Message); }
    }

    public static void CancelMidnight(Context context)
    {
        try { ((AlarmManager)context.GetSystemService(Context.AlarmService)!).Cancel(MidnightIntent(context)); }
        catch (Exception ex) { Store.Log("Midnight alarm: " + ex.Message); }
    }
}

/// <summary>A new day: the home screen widgets get their due texts for today, and the alarm is set for tomorrow.</summary>
[BroadcastReceiver(Exported = false)]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Started by Android's alarm.")]
public class MidnightReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null) return;
        ListWidgetProvider.RefreshAll(context);
        Alarms.ScheduleMidnight(context);
    }
}

/// <summary>The reminder alarm rang: show what's due (the app may not be running) and set the next alarm.</summary>
[BroadcastReceiver(Exported = false)]
public class ReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null) return;
        Alarms.EnsureData(context);
        var due = Reminders.TakeDue(Store.AllItems(), DateTime.Now);
        if (due.Count > 0)
        {
            Store.SaveNow(); // the reminders that rang are removed
            var (title, text) = Reminders.Describe(due);
            Notifications.Show(context, title, text);
        }
        // Also save what changed without ringing (old reminders removed, or the app's own check when Android started it),
        // as the system can end the process right after this method
        else Store.Flush();
        Alarms.ScheduleNext(context);
    }
}

/// <summary>
/// Sets the reminder alarm again when the system dropped or moved it: after a restart of the phone, after an update
/// of the app, and when the clock or the time zone changed (reminders are wall-clock times).
/// </summary>
[BroadcastReceiver(Exported = true, Enabled = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced, Intent.ActionTimeChanged, Intent.ActionTimezoneChanged })]
public class BootReceiver : BroadcastReceiver
{
    static readonly string[] Handled = { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced, Intent.ActionTimeChanged, Intent.ActionTimezoneChanged };

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || Array.IndexOf(Handled, intent?.Action) < 0) return;
        Alarms.EnsureData(context);
        Alarms.ScheduleNext(context);
        // the home screen widgets too: Android forgets alarms on a restart, and a new date changes the due texts
        ListWidgetProvider.RefreshAll(context);
        Alarms.ScheduleMidnight(context);
    }
}