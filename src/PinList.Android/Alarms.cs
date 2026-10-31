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
            Store.SaveNow(); // marked as shown
            var (title, text) = Reminders.Describe(due);
            Notifications.Show(context, title, text);
        }
        // Also save what the app's own check marked as shown (when Android started the app for this alarm),
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
    }
}