using System;
using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

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
        base.OnCreate(savedInstanceState);
        // Reminders are notifications; since Android 13 the app must ask before showing them
        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
            RequestPermissions(new[] { Manifest.Permission.PostNotifications }, 1);
        Alarms.ScheduleNext(this);
    }

    protected override void OnStop()
    {
        (Avalonia.Application.Current as PhoneApp)?.View?.SaveTyping();
        Store.Flush(); // the system may end the app any time after this
        Alarms.ScheduleNext(this);
        base.OnStop();
    }
}
