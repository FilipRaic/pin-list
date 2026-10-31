using System;
using Android.App;
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
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder);
}
