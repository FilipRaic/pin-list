using System;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using PinList.Sync;

namespace PinList.Android;

[Activity(
    Label = "PinList",
    Theme = "@style/PinListTheme",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AndroidGoogleAuth.CurrentActivity = this; // Google's consent page opens over this screen
        base.OnCreate(savedInstanceState);
        // Reminders are notifications; since Android 13 the app must ask before showing them
        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
            RequestPermissions(new[] { Manifest.Permission.PostNotifications }, 1);
        Alarms.ScheduleNext(this);
    }

    protected override void OnResume()
    {
        base.OnResume();
        AndroidGoogleAuth.CurrentActivity = this;
        _ = SyncService.Current?.SyncNow(); // back in the app: get what changed elsewhere (spec 9.9)
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if ((PhoneApp.Auth as AndroidGoogleAuth)?.OnActivityResult(requestCode, resultCode, data) != true)
            base.OnActivityResult(requestCode, resultCode, data);
    }

    protected override void OnDestroy()
    {
        if (AndroidGoogleAuth.CurrentActivity == this) AndroidGoogleAuth.CurrentActivity = null;
        base.OnDestroy();
    }

    protected override void OnStop()
    {
        (Avalonia.Application.Current as PhoneApp)?.View?.SaveTyping();
        Store.Flush(); // the system may end the app any time after this
        Alarms.ScheduleNext(this);
        // changes not uploaded yet go out as soon as there's a network, even if the app is closed now (spec 9.9)
        if (SyncService.Current?.HasPendingUploads == true) BackgroundSync.SyncSoon(this);
        BackgroundSync.Update(this);
        base.OnStop();
    }
}
