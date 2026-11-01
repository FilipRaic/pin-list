using System;
using Android.App;
using Android.OS;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace PinList.Android;

/// <summary>Starts PinList on Android: data in the app's private storage, reminders as notifications.</summary>
[Application]
public class MainApplication : AvaloniaAndroidApplication<PhoneApp>
{
    protected MainApplication(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer) { }

    public override void OnCreate()
    {
        PhoneApp.DataDir = FilesDir!.AbsolutePath;
        PhoneApp.ShowNotification = (title, text) => Notifications.Show(this, title, text);
        PhoneApp.Saved = () => Alarms.ScheduleNext(this);
        PhoneApp.SyncSettingsChanged = () => BackgroundSync.Update(this);
        // Google sync (also for background runs, when no screen was ever opened): sign-in through Play services,
        // and the phone's name for sync messages
        PhoneApp.Auth = new AndroidGoogleAuth(this);
        PhoneApp.DeviceName = $"{Build.Manufacturer} {Build.Model}".Trim();
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder);
}
