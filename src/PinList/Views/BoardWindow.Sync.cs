using System;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using PinList.Sync;

namespace PinList;

/// <summary>The widget's Google sync icon and "Sync this window" (spec 4.3, 4.4).</summary>
public partial class BoardWindow
{
    SyncStatus _syncStatus = SyncStatus.Hidden;

    /// <summary>Shows the list's sync status in the header (nothing while signed out).</summary>
    internal void RefreshSync()
    {
        var state = SyncService.Current?.StateOf(Board) ?? new ListSyncState(SyncStatus.Hidden, null, null);
        var look = SyncIcon.For(state, DateTime.UtcNow);
        SyncBtn.IsVisible = look != null;
        if (look is { } l)
        {
            ((PathIcon)SyncBtn.Content!).Data = (Geometry)this.FindResource(l.IconKey)!;
            ToolTip.SetTip(SyncBtn, l.Tip);
            AutomationProperties.SetName(SyncBtn, l.Tip);
            SyncBtn.Classes.Set("danger", l.Danger);
            SyncBtn.Classes.Set("spinning", state.Status == SyncStatus.Syncing);
            if (state.Status == SyncStatus.Problem && _syncStatus != SyncStatus.Problem)
                A11y.Announce(this, "Sync problem: " + l.Tip);
        }
        _syncStatus = state.Status;
    }

    void Sync_Click(object? sender, RoutedEventArgs e)
    {
        var state = SyncService.Current!.StateOf(Board);
        if (state.Status == SyncStatus.Problem)
        {
            _ = App.Instance.ExplainSyncProblem(this, state.Problem!);
            return;
        }
        var menu = NewMenu(SyncBtn, PlacementMode.Bottom);
        menu.Items.Add(state.Status == SyncStatus.Local
            ? Item("Turn on sync for this window", () => _ = App.Instance.SetBoardSynced(Board, true, this))
            : Item("Sync now", () => _ = SyncService.Current!.SyncNow()));
        Open(menu, SyncBtn);
    }

    /// <summary>"Sync this window" (checkable), in its own group of the ⋯ menu; only in builds that can sync.</summary>
    void AddSyncMenuItem(ContextMenu menu)
    {
        if (SyncService.Current?.IsAvailable != true) return;
        menu.Items.Add(new Separator());
        var sync = Item("Sync this window", () => _ = App.Instance.SetBoardSynced(Board, !Board.Synced, this));
        sync.ToggleType = MenuItemToggleType.CheckBox;
        sync.IsChecked = Board.Synced;
        menu.Items.Add(sync);
    }
}
