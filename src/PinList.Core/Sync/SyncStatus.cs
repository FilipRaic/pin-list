using System;

namespace PinList.Sync;

/// <summary>What a list's sync icon shows (spec 4.4). Hidden: signed out, no icon at all.</summary>
public enum SyncStatus { Hidden, Local, UpToDate, Syncing, Waiting, Problem }

/// <summary>A list's sync state for its icon: when it last synced, and the problem text if there is one.</summary>
public sealed record ListSyncState(SyncStatus Status, DateTime? LastSyncUtc, string? Problem);

/// <summary>Result of a background sync run (Android): whether local data changed, and how the run ended.</summary>
public sealed record HeadlessResult(bool Changed, SyncStatus Status);
