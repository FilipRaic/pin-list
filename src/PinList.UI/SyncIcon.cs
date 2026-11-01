using System;
using PinList.Sync;

namespace PinList;

/// <summary>A list's sync status as its header icon and tooltip (spec 4.4), shared by the desktop and the phone.</summary>
public static class SyncIcon
{
    /// <summary>The icon's resource key, tooltip (also the screen reader name) and whether it's shown in red. Null: no icon.</summary>
    public static (string IconKey, string Tip, bool Danger)? For(ListSyncState s, DateTime utcNow) => s.Status switch
    {
        SyncStatus.Local => ("Icon.CloudOff", "Not synced", false),
        SyncStatus.UpToDate => ("Icon.CloudCheck", s.LastSyncUtc == null ? "Synced" : "Synced · " + Ago(s.LastSyncUtc, utcNow), false),
        SyncStatus.Syncing => ("Icon.Sync", "Syncing…", false),
        SyncStatus.Waiting => ("Icon.CloudWaiting", "Offline · changes will sync later", false),
        SyncStatus.Problem => ("Icon.CloudProblem", s.Problem ?? "", true),
        _ => null,
    };

    /// <summary>"just now", "5 min ago", "2 h ago", "3 days ago" (or "never").</summary>
    public static string Ago(DateTime? utc, DateTime utcNow)
    {
        if (utc is not { } t) return "never";
        var ago = utcNow - t;
        if (ago < TimeSpan.FromMinutes(1)) return "just now";
        if (ago < TimeSpan.FromHours(1)) return $"{(int)ago.TotalMinutes} min ago";
        if (ago < TimeSpan.FromDays(1)) return $"{(int)ago.TotalHours} h ago";
        int days = (int)ago.TotalDays;
        return days == 1 ? "1 day ago" : $"{days} days ago";
    }
}
