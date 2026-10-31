using Android.App;
using Android.Content;
using System;
using Android.OS;

namespace PinList.Android;

/// <summary>Reminders as Android notifications; tapping one opens PinList.</summary>
static class Notifications
{
    const string Channel = "reminders";

    public static void Show(Context context, string title, string text)
    {
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            manager.CreateNotificationChannel(new NotificationChannel(Channel, "Reminders", NotificationImportance.High)
            {
                Description = "Reminders you set on items",
            });

        var open = PendingIntent.GetActivity(context, 0,
            new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var notification = new Notification.Builder(context, Channel)
            .SetSmallIcon(Resource.Drawable.icon)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetStyle(new Notification.BigTextStyle().BigText(text))
            .SetContentIntent(open)
            .SetAutoCancel(true)
            .Build();
        // a new id each time, also after the app was ended in between (so one reminder never replaces another)
        manager.Notify((int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % int.MaxValue), notification);
    }
}
