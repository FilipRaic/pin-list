using Android.App;
using Android.Content;
using System;
using Android.OS;

namespace PinList.Android;

/// <summary>Reminders as Android notifications; tapping one opens PinList.</summary>
static class Notifications
{
    const string Channel = "reminders";

    const string SyncChannel = "sync";
    const string Prefs = "sync", SignInShown = "signInNoticeShown";
    const int SignInId = 1;

    public static void Show(Context context, string title, string text)
    {
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            manager.CreateNotificationChannel(new NotificationChannel(Channel, "Reminders", NotificationImportance.High)
            {
                Description = "Reminders you set on items",
            });
        // a new id each time, also after the app was ended in between (so one reminder never replaces another)
        manager.Notify((int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % int.MaxValue), Build(context, Channel, title, text));
    }

    /// <summary>
    /// Background sync needs the user (Google access removed): one notice that opens PinList, not repeated until a
    /// sync works again (spec 9.9).
    /// </summary>
    public static void ShowSignInAgain(Context context)
    {
        var prefs = context.GetSharedPreferences(Prefs, FileCreationMode.Private)!;
        if (prefs.GetBoolean(SignInShown, false)) return;
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            manager.CreateNotificationChannel(new NotificationChannel(SyncChannel, "Sync", NotificationImportance.Default)
            {
                Description = "When Google sync needs you",
            });
        manager.Notify(SignInId, Build(context, SyncChannel, "PinList", "PinList sync stopped. Tap to sign in again."));
        prefs.Edit()!.PutBoolean(SignInShown, true)!.Apply();
    }

    /// <summary>A sync worked: the next problem may notify again.</summary>
    public static void SyncWorks(Context context) =>
        context.GetSharedPreferences(Prefs, FileCreationMode.Private)!.Edit()!.PutBoolean(SignInShown, false)!.Apply();

    static Notification Build(Context context, string channel, string title, string text)
    {
        var open = PendingIntent.GetActivity(context, 0,
            new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        return new Notification.Builder(context, channel)
            .SetSmallIcon(Resource.Drawable.icon)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetStyle(new Notification.BigTextStyle().BigText(text))
            .SetContentIntent(open)
            .SetAutoCancel(true)
            .Build()!;
    }
}
