using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using Android.Content;
using Android.Runtime;
using AndroidX.Work;
using PinList.Sync;

namespace PinList.Android;

/// <summary>
/// Sync while PinList is closed (spec 9.9): WorkManager runs <see cref="SyncWorker"/> about every 15 minutes with a
/// network connection, and once right away when the user leaves the app with changes not yet uploaded.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Runs inside Android's WorkManager.")]
public static class BackgroundSync
{
    const string Periodic = "pinlist-sync";
    const string Soon = "pinlist-sync-now";

    static Constraints NeedsNetwork() =>
        new Constraints.Builder().SetRequiredNetworkType(NetworkType.Connected!).Build();

    /// <summary>The periodic job exists only while signed in with at least one synced list.</summary>
    public static void Update(Context context)
    {
        try
        {
            var work = WorkManager.GetInstance(context);
            if (SyncService.Current?.NeedsBackgroundSync == true)
            {
                var request = new PeriodicWorkRequest.Builder(typeof(SyncWorker), TimeSpan.FromMinutes(15))
                    .SetConstraints(NeedsNetwork())
                    .Build();
                work.EnqueueUniquePeriodicWork(Periodic, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
            }
            else
            {
                work.CancelUniqueWork(Periodic);
                work.CancelUniqueWork(Soon);
            }
        }
        catch (Exception ex) { Store.Log("Background sync: " + ex.Message); }
    }

    /// <summary>One run as soon as there's a network (leaving the app with changes not uploaded yet).</summary>
    public static void SyncSoon(Context context)
    {
        try
        {
            var request = new OneTimeWorkRequest.Builder(typeof(SyncWorker))
                .SetConstraints(NeedsNetwork())
                .SetBackoffCriteria(BackoffPolicy.Exponential!, TimeSpan.FromSeconds(30))
                .Build();
            WorkManager.GetInstance(context).EnqueueUniqueWork(Soon, ExistingWorkPolicy.Replace!, (OneTimeWorkRequest)request);
        }
        catch (Exception ex) { Store.Log("Background sync: " + ex.Message); }
    }
}

/// <summary>One background sync pass (with the open app's data when PinList's screen is running).</summary>
[Register("com.pinlist.app.SyncWorker")]
[ExcludeFromCodeCoverage(Justification = "Runs inside Android's WorkManager.")]
public sealed class SyncWorker : Worker
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public SyncWorker(Context context, WorkerParameters parameters) : base(context, parameters) { }

    public override Result DoWork()
    {
        var context = ApplicationContext!;
        var auth = PhoneApp.Auth ?? new AndroidGoogleAuth(context);
        HeadlessResult result;
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            result = SyncService.RunHeadless(context.FilesDir!.AbsolutePath, auth,
                () => new DriveCloudStore(Http, auth.AccessToken), PhoneApp.DeviceName, PhoneApp.OnUiThread, limit.Token)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Store.Log("Background sync failed: " + ex.Message);
            return Result.InvokeRetry()!;
        }

        // a reminder set on another device also rings here, even if PinList is never opened
        if (result.Changed) Alarms.ScheduleNext(context);

        switch (result.Status)
        {
            case SyncStatus.Waiting:
                return Result.InvokeRetry()!;
            case SyncStatus.Problem:
                if (SyncService.Current?.NeedsSignIn != false) Notifications.ShowSignInAgain(context);
                return Result.InvokeFailure()!;
            default:
                Notifications.SyncWorks(context);
                return Result.InvokeSuccess()!;
        }
    }
}
