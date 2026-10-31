using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace PinList;

/// <summary>
/// The phone app (Android). The platform project sets <see cref="DataDir"/> and <see cref="ShowNotification"/>,
/// then starts it.
/// </summary>
public class PhoneApp : Application
{
    /// <summary>Where data.json lives (the app's private storage). Set by the platform before start.</summary>
    public static string? DataDir { get; set; }

    /// <summary>Shows a system notification for due reminders (title, text). Set by the platform.</summary>
    public static Action<string, string>? ShowNotification { get; set; }

    /// <summary>Called after every save (the platform schedules the reminder alarms then).</summary>
    public static Action? Saved { get; set; }

    public PhoneView? View { get; private set; }
    public Reminders? ReminderService { get; private set; }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://PinList.UI/")) { Source = new Uri("avares://PinList.UI/Styles/Common.axaml") });
    }

    [ExcludeFromCodeCoverage(Justification = "Only runs inside the Android app; tests call Start themselves.")]
    public override void OnFrameworkInitializationCompleted()
    {
        var view = Start();
        if (ApplicationLifetime is ISingleViewApplicationLifetime single) single.MainView = view;
        else if (ApplicationLifetime is IActivityApplicationLifetime activity) activity.MainViewFactory = () => View ?? Start();
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Loads the data and makes the phone screen.</summary>
    public PhoneView Start()
    {        UiTimers.Factory = () => new AvaloniaUiTimer();
        Themes.SystemThemeName = "Use phone setting";
        Themes.SystemDarkSource = () => PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
        Store.Saver = new DebouncedSaveScheduler(() => { Store.SaveNow(); Saved?.Invoke(); });
        if (DataDir != null) Store.UseDirectory(DataDir);

        View = new PhoneView();
        Store.Notify = (message, _) => View.ShowMessageForStore(message);
        PhoneView.SetTheme = SetTheme;

        if (!Store.Load() || Store.Data.Boards.Count == 0)
        {
            // First start: one empty list to begin with
            Store.Data.Boards.Add(Store.DefaultBoard(new WorkArea(0, 0, 1280, 800), Themes.SystemUsesDark() ? "Dark" : "Light"));
            Store.SaveNow();
        }
        ApplyTheme();
        View.Reload();

        ReminderService?.Stop();
        ReminderService = new Reminders();
        ReminderService.Due += due =>
        {
            var (title, text) = Reminders.Describe(due);
            ShowNotification?.Invoke(title, text);
        };
        ReminderService.Start();
        if (PlatformSettings != null) PlatformSettings.ColorValuesChanged += (_, _) => ApplyTheme();
        return View;
    }

    internal void SetTheme(string key)
    {
        Store.Data.Settings.Theme = key;
        Store.MarkDirty();
        ApplyTheme();
    }

    internal void ApplyTheme()
    {
        Themes.ApplyBoard(Resources, Themes.Get("Dark"), false); // defaults; the board screen sets its own
        bool dark = Themes.IsDark(Store.Data.Settings.Theme);
        Themes.ApplyChrome(Resources, dark);
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        View!.App_ThemeOrDataChanged(); // the view is made before the theme is first applied
    }
}
