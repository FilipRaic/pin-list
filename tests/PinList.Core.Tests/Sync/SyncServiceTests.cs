using PinList;
using PinList.Sync;
using Xunit;

namespace PinList.Tests.Sync;

/// <summary>
/// The sync service with simulated devices. Store is static, so each device has its own data folder and data, and
/// the test switches Store to a device before it acts as that device. All devices share one fake cloud.
/// </summary>
public class SyncServiceTests : StoreTestBase
{
    readonly FakeCloud _cloud = new();
    DateTime _now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>One device: its own folder, data, sign-in and SyncService.</summary>
    sealed class Device
    {
        public readonly string Name, Dir;
        public readonly AppData Data = new();
        public readonly FakeAuth Auth = new();
        public SyncService Service = null!;
        public readonly List<string> Messages = new();

        public Device(string name, string dir) { Name = name; Dir = dir; }

        public void Use() => Store.Use(Dir, Data);
    }

    readonly Device A, B;
    Device? _active;

    public SyncServiceTests()
    {
        A = NewDevice("A");
        B = NewDevice("B");
        Use(A);
    }

    public override void Dispose()
    {
        SyncService.Current = null;
        base.Dispose();
    }

    Device NewDevice(string name)
    {
        var d = new Device(name, Path.Combine(Dir, name));
        Store.Use(d.Dir, d.Data);
        d.Service = new SyncService(d.Auth, () => _cloud, "Device " + name, () => _now = _now.AddSeconds(1));
        d.Service.Message += d.Messages.Add;
        _cloud.Authorized = () => _active?.Auth.TokenValid ?? true;
        return d;
    }

    Device Use(Device d)
    {
        d.Use();
        _active = d;
        _cloud.Authorized = () => d.Auth.TokenValid;
        return d;
    }

    static T Await<T>(Task<T> t) => t.GetAwaiter().GetResult();
    static void Await(Task t) => t.GetAwaiter().GetResult();

    bool Sync(Device d) { Use(d); return Await(d.Service.RunOnce(CancellationToken.None)); }
    void SignIn(Device d) { Use(d); Assert.True(Await(d.Service.SignIn(CancellationToken.None))); }

    Board NewList(Device d, string title, params string[] items)
    {
        Use(d);
        var b = new Board { Title = title };
        var c = new Category { Name = "General" };
        foreach (var t in items) c.Items.Add(new TodoItem { Text = t });
        b.Categories.Add(c);
        d.Data.Boards.Add(b);
        return b;
    }

    /// <summary>A synced list "Shared" (items a, b) that both A and B have.</summary>
    Board Shared()
    {
        SignIn(A);
        SignIn(B);
        var board = NewList(A, "Shared", "a", "b");
        Await(A.Service.SetSynced(board, true));
        Sync(B);
        Assert.NotNull(BoardOf(B, board.Id));
        return board;
    }

    static Board? BoardOf(Device d, string id) => d.Data.Boards.FirstOrDefault(b => b.Id == id);
    static string Texts(Board b) => string.Join(",", b.Categories.SelectMany(c => c.Items).Select(i => i.Text));
    static TodoItem Item(Board b, string text) => b.Categories.SelectMany(c => c.Items).Single(i => i.Text == text);
    string FileOf(Device d, Board b) => $"w-{b.Id}.{d.Data.SyncInfo!.DeviceId}.json";

    // ================= Signed out / signing in =================

    [Fact]
    public void Signed_out_shows_nothing_and_uploads_nothing()
    {
        var b = NewList(A, "Mine", "x");
        b.Synced = true; // e.g. paused after signing out
        Assert.Null(A.Service.Account);
        Assert.Equal(SyncStatus.Hidden, A.Service.StateOf(b).Status);
        Assert.False(Sync(A));
        Assert.Equal(0, _cloud.Calls);
        Assert.False(A.Service.NeedsBackgroundSync);
        Assert.False(A.Service.HasPendingUploads);
    }

    [Fact]
    public void Sign_in_does_not_turn_on_any_list()
    {
        var b = NewList(A, "Mine", "x");
        int changed = 0;
        A.Service.Changed += () => changed++;
        SignIn(A);
        Assert.Equal("me@example.com", A.Service.Account!.Email);
        Assert.False(b.Synced);
        Assert.Equal(SyncStatus.Local, A.Service.StateOf(b).Status);
        Sync(A);
        Assert.Empty(_cloud.Names);
        Assert.True(changed > 0);
        Assert.Equal("me@example.com", SyncState.Load(A.Dir).Account!.Email);
    }

    [Fact]
    public void Cancelled_sign_in_changes_nothing()
    {
        A.Auth.NextAccount = null;
        Assert.False(Await(A.Service.SignIn(CancellationToken.None)));
        Assert.Null(A.Service.Account);
    }

    [Fact]
    public void Unavailable_auth_disables_everything()
    {
        var b = NewList(A, "Mine", "x");
        A.Auth.IsAvailable = false;
        Assert.False(A.Service.IsAvailable);
        Assert.Equal("Sync isn't available in this build.", A.Service.UnavailableText);
        Assert.False(Await(A.Service.SignIn(CancellationToken.None)));
        Assert.Equal(0, A.Auth.SignIns);
        Await(A.Service.SetSynced(b, true));
        Assert.False(b.Synced);
        Assert.Equal(SyncStatus.Hidden, A.Service.StateOf(b).Status);
    }

    // ================= Turning sync on, edits =================

    [Fact]
    public void Turning_on_uploads_and_the_list_appears_on_the_other_device()
    {
        SignIn(A);
        var b = NewList(A, "Shared", "a", "b");
        b.Categories[0].Items[0].ReminderAt = new DateTime(2026, 11, 2, 9, 0, 0);
        Await(A.Service.SetSynced(b, true));
        Assert.True(b.Synced);
        Assert.Equal(SyncStatus.UpToDate, A.Service.StateOf(b).Status);
        Assert.True(_cloud.Has(FileOf(A, b)));
        Assert.Equal("Device A", _cloud.Copy(FileOf(A, b)).DeviceName);

        var added = new List<Board>();
        Actions.BoardAdded += added.Add;
        try { SignIn(B); } // signing in syncs right away (spec 4.2: synced lists appear)
        finally { Actions.BoardAdded -= added.Add; }

        var onB = BoardOf(B, b.Id)!;
        Assert.Same(onB, added.Single());
        Assert.True(onB.Synced);
        Assert.Equal("a,b", Texts(onB));
        Assert.Equal(new DateTime(2026, 11, 2, 9, 0, 0), onB.Categories[0].Items[0].ReminderAt);
        Assert.Equal(new WorkArea(0, 0, 1280, 800), B.Service.WorkArea());
        Assert.InRange(onB.X, 0, 1280);
        Assert.True(_cloud.Has(FileOf(B, onB)));
        Assert.False(Sync(B)); // nothing new
    }

    [Fact]
    public void Turning_on_while_signed_out_signs_in_first()
    {
        var b = NewList(A, "Shared", "a");
        Await(A.Service.SetSynced(b, true));
        Assert.Equal(1, A.Auth.SignIns);
        Assert.True(b.Synced);
        Assert.True(_cloud.Has(FileOf(A, b)));

        var c = NewList(A, "Other", "x");
        A.Auth.NextAccount = null; // the next sign-in is cancelled
        Await(A.Service.SignOut(false));
        Await(A.Service.SetSynced(c, true));
        Assert.False(c.Synced);
    }

    [Fact]
    public void Edits_reach_the_other_device()
    {
        var b = Shared();
        Use(A);
        Item(b, "a").Text = "a from A";
        Sync(A);
        Assert.True(Sync(B));
        var onB = BoardOf(B, b.Id)!;
        Assert.Equal("a from A,b", Texts(onB));

        Use(B);
        onB.Categories[0].Items.Add(new TodoItem { Text = "from B" });
        Sync(B);
        Assert.True(Sync(A));
        Assert.Equal("a from A,b,from B", Texts(b));
    }

    [Fact]
    public void Remote_changes_are_not_in_the_undo_history()
    {
        var b = Shared();
        Use(A);
        Item(b, "a").Text = "changed";
        Sync(A);
        Use(B);
        Actions.ClearUndo();
        Sync(B);
        Assert.False(Actions.CanUndo);
    }

    [Fact]
    public void Undo_after_a_remote_merge_is_a_new_local_change()
    {
        var b = Shared();
        Use(B);
        var onB = BoardOf(B, b.Id)!;
        Actions.ClearUndo();
        Actions.SetItemText(Item(onB, "b"), "b on B");
        Sync(B);
        Use(A);
        Item(b, "a").Text = "a on A";
        Sync(A);
        Sync(B);
        Assert.Equal("a on A,b on B", Texts(onB));

        Use(B);
        Assert.True(Actions.Undo());
        Sync(B);
        Sync(A);
        Assert.Equal("a on A,b", Texts(b));
    }

    [Fact]
    public void Remote_change_waits_while_item_is_edited()
    {
        var b = Shared();
        Use(A);
        Item(b, "a").Text = "remote";
        Sync(A);

        Use(B);
        var onB = BoardOf(B, b.Id)!;
        var editing = Item(onB, "a");
        editing.IsEditing = true;
        Assert.False(Sync(B));
        Assert.Equal("a", editing.Text);

        editing.IsEditing = false;
        Assert.True(Sync(B));
        Assert.Equal("remote", editing.Text);
    }

    [Fact]
    public void Custom_busy_check_holds_back_changes()
    {
        var b = Shared();
        Use(A);
        Item(b, "a").Text = "remote";
        Sync(A);
        Use(B);
        var dragged = BoardOf(B, b.Id)!.Categories[0];
        B.Service.IsBusy = o => o == dragged;
        Assert.False(Sync(B));
        B.Service.IsBusy = _ => false;
        Assert.True(Sync(B));
    }

    [Fact]
    public void Concurrent_move_and_edit_leaves_one_copy()
    {
        SignIn(A);
        SignIn(B);
        var one = NewList(A, "One", "x");
        var two = NewList(A, "Two", "y");
        Await(A.Service.SetSynced(one, true));
        Await(A.Service.SetSynced(two, true));
        Sync(B);

        Use(B);
        Item(BoardOf(B, one.Id)!, "x").Text = "x edited on B";
        Sync(B);
        Use(A);
        var x = Item(one, "x");
        one.Categories[0].Items.Remove(x);
        two.Categories[0].Items.Add(x); // moved without seeing B's edit
        Sync(A);
        Sync(B);
        Sync(A);

        foreach (var d in new[] { A, B })
            Assert.Single(d.Data.Boards.SelectMany(l => l.Categories).SelectMany(c => c.Items), i => i.Id == x.Id);
    }

    // ================= Deleting a list =================

    [Fact]
    public void Delete_list_leaves_a_marker_and_removes_it_everywhere()
    {
        var b = Shared();
        Use(A);
        A.Service.Start();
        try
        {
            Actions.DeleteBoard(b);
            Sync(A);
        }
        finally { A.Service.Stop(); }
        var marker = _cloud.Copy(FileOf(A, b));
        Assert.True(marker.Deleted);
        Assert.True(marker.IsMarker);

        var removed = new List<(Board, bool)>();
        Actions.BoardDeleted += (x, user) => removed.Add((x, user));
        Use(B);
        Store.SaveNow();
        Assert.True(Sync(B));
        Assert.Null(BoardOf(B, b.Id));
        Assert.False(removed.Single().Item2); // not by the user on this device
        Assert.True(Directory.GetFiles(Path.Combine(B.Dir, "backups")).Length > 0);
        Assert.True(_cloud.Copy(FileOf(B, b)).IsMarker); // B's copy is removed from the cloud too
        Assert.Contains($"Sync removed list {b.Id}", File.ReadAllText(Path.Combine(B.Dir, "log.txt")));

        Assert.False(Sync(A)); // A doesn't get it back from B's old file
        Assert.Null(BoardOf(A, b.Id));
    }

    [Fact]
    public void Deleting_a_local_list_sends_nothing()
    {
        SignIn(A);
        var b = NewList(A, "Mine", "x");
        A.Service.Start();
        try { Actions.DeleteBoard(b); }
        finally { A.Service.Stop(); }
        Sync(A);
        Assert.Empty(_cloud.Names);
    }

    [Fact]
    public void Deleted_list_edited_elsewhere_is_kept_locally_with_message()
    {
        var b = Shared();
        Use(B);
        var onB = BoardOf(B, b.Id)!;
        Item(onB, "a").Text = "edited on B";
        B.Data.Archive.Add(new ArchivedItem { Text = "done on B", BoardId = b.Id });
        Store.SaveNow(); // stamped, not uploaded yet

        Use(A);
        A.Service.Start();
        try { Actions.DeleteBoard(b); }
        finally { A.Service.Stop(); }
        Sync(A);

        Assert.True(Sync(B));
        Assert.Same(onB, B.Data.Boards.Single());
        Assert.False(onB.Synced);
        Assert.NotEqual(b.Id, onB.Id);
        Assert.Equal("edited on B,b", Texts(onB));
        Assert.Equal(onB.Id, B.Data.Archive.Single().BoardId); // its archive stays with it
        Assert.Equal("“Shared” was deleted on another device. Your recent changes on this device were kept in a local window.",
            B.Messages.Single());
        Assert.False(Sync(B)); // and it stays local
        Assert.False(onB.Synced);
    }

    // ================= Turning sync off =================

    [Fact]
    public void Turning_off_sends_last_edits_then_the_flag()
    {
        var b = Shared();
        Use(A);
        Item(b, "a").Text = "last edit";
        Await(A.Service.SetSynced(b, false));
        var file = _cloud.Copy(FileOf(A, b));
        Assert.False(file.Synced);
        Assert.False(file.IsMarker); // the last edits go out with the flag
        Assert.Equal(SyncStatus.Local, A.Service.StateOf(b).Status);

        Assert.True(Sync(B));
        var onB = BoardOf(B, b.Id)!;
        Assert.False(onB.Synced);
        Assert.Equal("last edit,b", Texts(onB));

        Sync(A); // the next pass strips A's copy to a marker
        Assert.True(_cloud.Copy(FileOf(A, b)).IsMarker);
        Assert.False(_cloud.Copy(FileOf(A, b)).Synced);
        Assert.False(Sync(A));
    }

    [Fact]
    public void Turning_off_while_offline_waits_then_sends_in_order()
    {
        var b = Shared();
        Use(A);
        _cloud.Offline = true;
        Item(b, "a").Text = "offline edit";
        Await(A.Service.SetSynced(b, false));
        Assert.Equal(SyncStatus.Waiting, A.Service.StateOf(b).Status);
        Assert.True(A.Service.HasPendingUploads);
        Assert.True(_cloud.Copy(FileOf(A, b)).Synced); // nothing sent yet

        _cloud.Offline = false;
        Sync(A);
        var file = _cloud.Copy(FileOf(A, b));
        Assert.False(file.Synced);
        Assert.Contains(file.Items, i => i.Text == "offline edit");
        Assert.Equal(SyncStatus.Local, A.Service.StateOf(b).Status);
    }

    [Fact]
    public void Other_device_stops_without_uploading_after_off()
    {
        var b = Shared();
        Use(B);
        var onB = BoardOf(B, b.Id)!;
        Item(onB, "b").Text = "unsent on B";
        Store.SaveNow();
        Use(A);
        Await(A.Service.SetSynced(b, false));

        Sync(B);
        Assert.False(onB.Synced);
        Assert.Contains("unsent on B", Texts(onB));
        var bFile = _cloud.Copy(FileOf(B, onB));
        Assert.True(bFile.IsMarker); // B's edit never went out
        Sync(A);
        Assert.DoesNotContain("unsent on B", Texts(b));
    }

    [Fact]
    public void Turning_on_again_elsewhere_brings_the_list_back_into_sync()
    {
        var b = Shared();
        Use(A);
        Await(A.Service.SetSynced(b, false));
        Sync(B);
        Sync(A);
        var onB = BoardOf(B, b.Id)!;
        Use(B);
        Item(onB, "a").Text = "edited while local";
        Await(B.Service.SetSynced(onB, true));
        Assert.True(Sync(A));
        Assert.True(b.Synced);
        Assert.Equal("edited while local,b", Texts(b));
    }

    // ================= Signing out =================

    [Fact]
    public void Sign_out_keeps_lists_and_pauses_sync()
    {
        var b = Shared();
        Use(A);
        Item(b, "a").Text = "sent at sign-out";
        Await(A.Service.SignOut(false));
        Assert.Equal(1, A.Auth.SignOuts);
        Assert.Null(A.Service.Account);
        Assert.True(b.Synced);
        Assert.Equal(SyncStatus.Hidden, A.Service.StateOf(b).Status);
        Assert.Contains(_cloud.Copy(FileOf(A, b)).Items, i => i.Text == "sent at sign-out");

        int calls = _cloud.Calls;
        Item(b, "b").Text = "while signed out";
        Assert.False(Sync(A));
        Assert.Equal(calls, _cloud.Calls);
    }

    [Fact]
    public void Sign_in_again_same_account_resumes()
    {
        var b = Shared();
        Use(A);
        Await(A.Service.SignOut(false));
        Item(b, "a").Text = "while signed out";
        SignIn(A);
        Assert.True(b.Synced);
        Sync(A);
        Sync(B);
        Assert.Equal("while signed out,b", Texts(BoardOf(B, b.Id)!));
    }

    [Fact]
    public void Sign_in_with_another_account_makes_lists_local()
    {
        var b = Shared();
        Use(A);
        Await(A.Service.SignOut(false));
        A.Auth.NextAccount = new GoogleAccount("user-2", "other@example.com");
        SignIn(A);
        Assert.False(b.Synced);
        Sync(A);
        Assert.Equal(SyncStatus.Local, A.Service.StateOf(b).Status);
    }

    [Fact]
    public void Delete_cloud_data_leaves_reset_marker_and_others_go_local()
    {
        var b = Shared();
        Use(A);
        Await(A.Service.SignOut(true));
        Assert.Equal(new[] { "reset.json" }, _cloud.Names);
        Assert.False(b.Synced);
        Assert.Empty(A.Messages);

        Assert.True(Sync(B));
        var onB = BoardOf(B, b.Id)!;
        Assert.False(onB.Synced);
        Assert.Equal("a,b", Texts(onB));
        Assert.Equal("Sync was turned off because the cloud data was deleted from another device.", B.Messages.Single());
        Assert.Equal(new[] { "reset.json" }, _cloud.Names);
        Sync(B);
        Assert.Single(B.Messages); // only once

        // A signs in again later: its own reset doesn't bring a message
        SignIn(A);
        Sync(A);
        Assert.Empty(A.Messages);
    }

    // ================= Problems =================

    [Fact]
    public void Newer_format_file_shows_problem_and_is_not_overwritten()
    {
        var b = Shared();
        Use(B);
        var newer = System.Text.Json.Nodes.JsonNode.Parse(_cloud.Read(FileOf(B, BoardOf(B, b.Id)!)))!;
        newer["format"] = 2;
        _cloud.Put(FileOf(B, BoardOf(B, b.Id)!), newer.ToJsonString());

        Use(A);
        Item(b, "a").Text = "not sent";
        Sync(A);
        var state = A.Service.StateOf(b);
        Assert.Equal(SyncStatus.Problem, state.Status);
        Assert.Equal("Update PinList to keep syncing this window.", state.Problem);
        Assert.DoesNotContain(_cloud.Copy(FileOf(A, b)).Items, i => i.Text == "not sent");
    }

    [Fact]
    public void Broken_file_is_logged_and_skipped()
    {
        var b = Shared();
        _cloud.Put($"w-{b.Id}.Z.json", "not json");
        _cloud.Put("unrelated.txt", "x");
        _cloud.Put("w-nodevice.json", "{}");
        Use(A);
        Sync(A);
        Assert.Equal(SyncStatus.UpToDate, A.Service.StateOf(b).Status);
        Assert.Contains("can't be read", File.ReadAllText(Path.Combine(A.Dir, "log.txt")));
    }

    [Fact]
    public void Transient_errors_back_off_then_show_problem()
    {
        var b = Shared();
        Use(A);
        A.Service.Start();
        try
        {
            Item(b, "a").Text = "kept";
            var before = Store.Serialize(A.Data);
            var delays = new List<double>();
            foreach (var error in new[] { CloudError.Offline, CloudError.RateLimited, CloudError.Server, CloudError.Offline, CloudError.Offline })
            {
                _cloud.FailNext.Enqueue(error);
                Await(A.Service.SyncNow());
                delays.Add(A.Service.RetryIn!.Value.TotalSeconds);
                if (delays.Count < 5) Assert.Equal(SyncStatus.Waiting, A.Service.StateOf(b).Status);
            }
            Assert.Equal(new double[] { 15, 30, 60, 120, 300 }, delays);
            Assert.Equal(SyncStatus.Problem, A.Service.StateOf(b).Status);
            Assert.Equal("Can't reach Google Drive.", A.Service.StateOf(b).Problem);
            Assert.Contains("\"kept\"", before);

            // the retry timer runs the next pass, which works again
            ManualTimer.Created.Single(t => t.Interval == TimeSpan.FromSeconds(300) && t.IsEnabled).Fire();
            Assert.Equal(SyncStatus.UpToDate, A.Service.StateOf(b).Status);
            Assert.Null(A.Service.RetryIn);
        }
        finally { A.Service.Stop(); }
    }

    [Fact]
    public void Other_cloud_errors_wait_and_retry()
    {
        var b = Shared();
        Use(A);
        _cloud.FailNext.Enqueue(CloudError.Other);
        Sync(A);
        Assert.Equal(SyncStatus.Waiting, A.Service.StateOf(b).Status);
    }

    [Fact]
    public void Unauthorized_asks_to_sign_in_again()
    {
        var b = Shared();
        Use(A);
        A.Auth.TokenValid = false;
        Sync(A);
        Assert.Equal(SyncStatus.Problem, A.Service.StateOf(b).Status);
        Assert.Equal("Google signed you out.", A.Service.StateOf(b).Problem);
        Assert.True(A.Service.NeedsSignIn);

        SignIn(A);
        Sync(A);
        Assert.Equal(SyncStatus.UpToDate, A.Service.StateOf(b).Status);
        Assert.False(A.Service.NeedsSignIn);
    }

    // ================= Timing =================

    [Fact]
    public void Upload_is_debounced_five_seconds_after_a_save()
    {
        var b = Shared();
        Use(A);
        A.Service.Start();
        try
        {
            var upload = ManualTimer.Created.Single(t => t.Interval == TimeSpan.FromSeconds(5));
            var every = ManualTimer.Created.Single(t => t.Interval == TimeSpan.FromSeconds(20));
            Assert.True(every.IsEnabled);
            Assert.False(upload.IsEnabled);

            Item(b, "a").Text = "saved";
            Store.SaveNow();
            Assert.True(upload.IsEnabled);
            Assert.DoesNotContain(_cloud.Copy(FileOf(A, b)).Items, i => i.Text == "saved");
            upload.Fire();
            Assert.False(upload.IsEnabled);
            Assert.Contains(_cloud.Copy(FileOf(A, b)).Items, i => i.Text == "saved");

            Item(b, "saved").Text = "every 20 s";
            every.Fire();
            Assert.Contains(_cloud.Copy(FileOf(A, b)).Items, i => i.Text == "every 20 s");
        }
        finally { A.Service.Stop(); }
        Store.SaveNow(); // stopped: no more timers
        Assert.All(ManualTimer.Created, t => Assert.False(t.IsEnabled));
    }

    [Fact]
    public void Exit_sync_stops_after_the_time_limit()
    {
        Shared();
        Use(A);
        _cloud.Gate = new TaskCompletionSource(); // the network hangs
        var started = DateTime.UtcNow;
        Await(A.Service.SyncBeforeExit(TimeSpan.FromMilliseconds(200)));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.True(SpinWait.SpinUntil(() => !A.Service.IsSyncing, 5000)); // the pass was cancelled too
        _cloud.Gate = null;
    }

    [Fact]
    public void Exit_sync_runs_a_pass()
    {
        var b = Shared();
        Use(A);
        Item(b, "a").Text = "at exit";
        Await(A.Service.SyncBeforeExit(TimeSpan.FromSeconds(3)));
        Assert.Contains(_cloud.Copy(FileOf(A, b)).Items, i => i.Text == "at exit");
    }

    [Fact]
    public void Run_once_works_without_start()
    {
        var b = Shared();
        Use(A);
        Item(b, "a").Text = "x";
        Assert.False(Sync(A));
        Assert.True(Sync(B));
        Assert.NotNull(A.Service.LastSyncUtc);
    }

    [Fact]
    public void Only_one_pass_at_a_time()
    {
        var b = Shared();
        Use(A);
        Item(b, "a").Text = "remote";
        Sync(A);

        Use(B);
        _cloud.Gate = new TaskCompletionSource();
        var first = B.Service.RunOnce(CancellationToken.None);
        var second = B.Service.RunOnce(CancellationToken.None);
        Assert.Equal(SyncStatus.Syncing, B.Service.StateOf(BoardOf(B, b.Id)!).Status);
        int callsWhileBlocked = _cloud.Calls;
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Equal(callsWhileBlocked, _cloud.Calls); // the second pass hasn't started

        var gate = _cloud.Gate;
        _cloud.Gate = null;
        gate.SetResult();
        Assert.True(Await(first) | Await(second));
        Assert.False(Await(first) & Await(second)); // the remote change was merged once
        Assert.Equal("remote,b", Texts(BoardOf(B, b.Id)!));
    }

    // ================= Background (Android) =================

    [Fact]
    public void Needs_background_sync_only_when_signed_in_with_a_synced_list()
    {
        var b = NewList(A, "Mine", "x");
        Assert.False(A.Service.NeedsBackgroundSync);
        SignIn(A);
        Assert.False(A.Service.NeedsBackgroundSync);
        Await(A.Service.SetSynced(b, true));
        Assert.True(A.Service.NeedsBackgroundSync);
        Assert.False(A.Service.HasPendingUploads);
        Item(b, "x").Text = "y";
        Assert.True(A.Service.HasPendingUploads);
    }

    [Fact]
    public void Headless_run_loads_syncs_and_saves()
    {
        var b = Shared();
        Use(B);
        Store.SaveNow();
        Use(A);
        Item(b, "a").Text = "while the phone sleeps";
        Sync(A);

        var result = Await(SyncService.RunHeadless(B.Dir, B.Auth, () => _cloud, "Device B", null, CancellationToken.None));
        Assert.True(result.Changed);
        Assert.Equal(SyncStatus.UpToDate, result.Status);
        Assert.Contains("while the phone sleeps", File.ReadAllText(Path.Combine(B.Dir, "data.json")));

        _cloud.Offline = true;
        result = Await(SyncService.RunHeadless(B.Dir, B.Auth, () => _cloud, "Device B", null, CancellationToken.None));
        Assert.Equal(SyncStatus.Waiting, result.Status);
    }

    [Fact]
    public void Headless_run_uses_live_data_when_the_app_runs()
    {
        var b = Shared();
        Use(A);
        Item(b, "a").Text = "live";
        Sync(A);

        Use(B);
        SyncService.Current = B.Service;
        int onUi = 0;
        var result = Await(SyncService.RunHeadless("unused", B.Auth, () => throw new InvalidOperationException(), "Device B",
            run => { onUi++; return run(); }, CancellationToken.None));
        Assert.Equal(1, onUi);
        Assert.True(result.Changed);
        Assert.Equal("live,b", Texts(BoardOf(B, b.Id)!)); // the open app's data
    }

    // ================= Error paths =================

    [Fact]
    public void Sign_in_that_fails_changes_nothing()
    {
        A.Auth.SignInError = new CloudException(CloudError.Other, "Google userinfo answered HTTP 401");
        Assert.False(Await(A.Service.SignIn(CancellationToken.None)));
        Assert.Null(A.Service.Account);
        Assert.Contains("Google sign-in failed", File.ReadAllText(Path.Combine(A.Dir, "log.txt")));
        // the user is told, with the reason (a silent failure looks like nothing happened)
        Assert.Equal("Google sign-in didn't work: Google userinfo answered HTTP 401.", A.Messages.Single());

        // any other error too, instead of escaping from a button's action unseen
        A.Auth.SignInError = new InvalidOperationException("Play services said no");
        Assert.False(Await(A.Service.SignIn(CancellationToken.None)));
        Assert.Equal("Google sign-in didn't work: Play services said no.", A.Messages.Last());

        A.Auth.SignInError = new CloudException(CloudError.Offline, "Can't reach Google: HttpRequestException");
        Await(A.Service.SignIn(CancellationToken.None));
        Assert.Equal("Can't sign in: no connection to Google.", A.Messages.Last());
    }

    [Fact]
    public void Delete_cloud_data_offline_keeps_everything()
    {
        var b = Shared();
        Use(A);
        _cloud.Offline = true;
        Await(A.Service.SignOut(true));
        Assert.Equal("Can't reach Google Drive. The cloud data wasn't deleted.", A.Messages.Single());
        Assert.NotNull(A.Service.Account);
        Assert.True(b.Synced);
        Assert.Equal(0, A.Auth.SignOuts);
    }

    [Fact]
    public void Sign_out_when_signed_out_does_nothing()
    {
        Await(A.Service.SignOut(false));
        Assert.Equal(0, A.Auth.SignOuts);
    }

    [Fact]
    public void Unreadable_reset_marker_counts_as_from_another_device()
    {
        var b = Shared();
        _cloud.Put("reset.json", "not json");
        Sync(A);
        Assert.False(b.Synced);
        Assert.Single(A.Messages);
    }

    [Fact]
    public void Switching_twice_to_the_same_state_does_nothing()
    {
        var b = Shared();
        Use(A);
        int calls = _cloud.Calls;
        Await(A.Service.SetSynced(b, true));
        Assert.Equal(calls, _cloud.Calls);
        var local = NewList(A, "Local", "x");
        Await(A.Service.SetSynced(local, false));
        Assert.Equal(calls, _cloud.Calls);
    }

    [Fact]
    public void Conflicts_are_logged_without_text()
    {
        var b = Shared();
        Use(B);
        Item(BoardOf(B, b.Id)!, "a").Text = "secret older";
        ChangeTracker.StampChanges(B.Data, _now); // on the test's clock, before A's edit
        Use(A);
        Item(b, "a").Text = "secret newer";
        Sync(A);
        Sync(B);
        var log = File.ReadAllText(Path.Combine(B.Dir, "log.txt"));
        Assert.Contains("newer version of", log);
        Assert.DoesNotContain("secret", log);
    }

    [Fact]
    public void Deleting_a_list_that_is_being_turned_off_still_deletes_it_everywhere()
    {
        var b = Shared();
        Use(A);
        _cloud.Offline = true;
        Await(A.Service.SetSynced(b, false));
        A.Service.Start();
        try { Actions.DeleteBoard(b); }
        finally { A.Service.Stop(); }
        _cloud.Offline = false;
        Sync(A);
        Assert.True(_cloud.Copy(FileOf(A, b)).Deleted);
        Sync(B);
        Assert.Null(BoardOf(B, b.Id));
    }

    [Fact]
    public void Headless_run_reports_problems()
    {
        Shared();
        Use(B);
        Store.SaveNow();
        B.Auth.TokenValid = false;
        var result = Await(SyncService.RunHeadless(B.Dir, B.Auth, () => _cloud, "Device B", null, CancellationToken.None));
        Assert.Equal(SyncStatus.Problem, result.Status);
        B.Auth.NextAccount = null;
        Await(B.Service.SignOut(false));
        result = Await(SyncService.RunHeadless(B.Dir, B.Auth, () => _cloud, "Device B", null, CancellationToken.None));
        Assert.Equal(SyncStatus.Hidden, result.Status);
    }

    [Fact]
    public void Headless_run_with_the_app_but_without_sync_does_nothing()
    {
        Shared();
        Use(B);
        SyncService.Current = null; // the app runs, but this build has no sync
        var live = Store.Data;
        int onUi = 0;
        var result = Await(SyncService.RunHeadless(B.Dir, B.Auth, () => _cloud, "Device B", run => { onUi++; return run(); }, CancellationToken.None));
        Assert.Equal(new HeadlessResult(false, SyncStatus.Hidden), result);
        Assert.Equal(0, onUi);
        Assert.Same(live, Store.Data); // the open screen's data wasn't replaced
    }

    [Fact]
    public void Headless_run_while_the_app_loads_leaves_the_merge_to_the_app()
    {
        var b = Shared();
        Use(B);
        Store.SaveNow();
        Use(A);
        Item(b, "a").Text = "remote";
        Sync(A);

        // the screen opens during the background pass and loads data.json itself
        AppData? screen = null;
        _cloud.OnCall = () =>
        {
            if (screen != null) return;
            screen = Store.Parse(File.ReadAllText(Path.Combine(B.Dir, "data.json")), out _);
            Store.Use(B.Dir, screen);
            _cloud.OnCall = null;
        };
        var result = Await(SyncService.RunHeadless(B.Dir, B.Auth, () => _cloud, "Device B", null, CancellationToken.None));
        Assert.NotNull(screen);
        Assert.False(result.Changed); // stopped: nothing went into data the app doesn't show

        // the app's own pass merges again into what the screen shows
        Store.Use(B.Dir, screen!);
        var app = new SyncService(B.Auth, () => _cloud, "Device B");
        Assert.True(Await(app.RunOnce(CancellationToken.None)));
        Assert.Equal("remote,b", Texts(screen!.Boards.Single(x => x.Id == b.Id)));
    }

    // ================= Final review findings =================

    [Fact]
    public void New_device_does_not_get_a_deleted_list_back_from_a_stale_file()
    {
        A.Data.SyncInfo = new DeviceInfo { DeviceId = "0000a" };
        B.Data.SyncInfo = new DeviceInfo { DeviceId = "zzzzb" };
        var b = Shared();
        // B goes offline for good; A deletes the list
        Use(A);
        A.Service.Start();
        try { Actions.DeleteBoard(b); } finally { A.Service.Stop(); }
        Sync(A);
        // a brand-new device reads the marker first, then B's stale copy
        var C = NewDevice("C");
        C.Data.SyncInfo = new DeviceInfo { DeviceId = "mmmmc" };
        var added = new List<Board>();
        Actions.BoardAdded += added.Add;
        try { SignIn(C); Sync(C); }
        finally { Actions.BoardAdded -= added.Add; }
        Assert.Null(BoardOf(C, b.Id));
        Assert.Empty(added); // not even for a moment
        Assert.Empty(C.Messages);
    }

    [Fact]
    public void Category_rename_survives_an_item_added_elsewhere()
    {
        var b = Shared();
        Use(A);
        b.Categories[0].Name = "Renamed on A";
        Store.SaveNow();
        Use(B);
        Actions.AddItem(BoardOf(B, b.Id)!.Categories[0], "added on B");
        Store.SaveNow();
        Sync(A); Sync(B); Sync(A);
        foreach (var d in new[] { A, B })
        {
            Assert.Equal("Renamed on A", BoardOf(d, b.Id)!.Categories[0].Name);
            Assert.Contains("added on B", Texts(BoardOf(d, b.Id)!));
        }
    }

    [Fact]
    public void List_title_survives_a_category_added_elsewhere()
    {
        var b = Shared();
        Use(A);
        b.Title = "Renamed on A";
        Store.SaveNow();
        Use(B);
        Actions.AddCategory(BoardOf(B, b.Id)!);
        Store.SaveNow();
        Sync(A); Sync(B); Sync(A);
        foreach (var d in new[] { A, B })
        {
            Assert.Equal("Renamed on A", BoardOf(d, b.Id)!.Title);
            Assert.Equal(2, BoardOf(d, b.Id)!.Categories.Count);
        }
    }

    [Fact]
    public void Restored_backup_does_not_overwrite_the_own_newer_cloud_copy()
    {
        var b = Shared();
        Use(A);
        var backup = Store.Serialize(A.Data);
        Actions.AddItem(b.Categories[0], "after backup");
        Store.SaveNow();
        Sync(A); // uploaded; B is offline
        var restored = Store.Parse(backup, out _); // data.json restored, sync-state.json stays
        A.Data.Boards.Clear();
        foreach (var x in restored.Boards) A.Data.Boards.Add(x);
        A.Data.SyncInfo = restored.SyncInfo;
        Sync(A);
        Sync(B);
        Assert.Contains("after backup", Texts(BoardOf(B, b.Id)!));
        Assert.Contains("after backup", Texts(BoardOf(A, b.Id)!));
    }

    [Fact]
    public void Counter_is_raised_from_the_cloud_when_both_local_files_went_back()
    {
        var b = Shared();
        Use(A);
        for (int i = 0; i < 5; i++) { Item(b, i == 0 ? "a" : "a" + (i - 1)).Text = "a" + i; Sync(A); }
        var high = A.Data.SyncInfo!.Counter;
        // data.json and sync-state.json both went back: a fresh service and an old counter
        File.Delete(Path.Combine(A.Dir, SyncState.FileName));
        A.Data.SyncInfo.Counter = 1;
        new SyncState { Account = new GoogleAccount("user-1", "me@example.com"), LastUserId = "user-1" }.Save(A.Dir);
        A.Service = new SyncService(A.Auth, () => _cloud, "Device A", () => _now = _now.AddSeconds(1));
        Sync(A);
        Assert.True(A.Data.SyncInfo.Counter >= high);
    }

    [Fact]
    public void Merged_changes_are_on_disk_before_the_files_count_as_merged()
    {
        var b = Shared();
        Use(A);
        Item(b, "a").Text = "remote edit";
        Sync(A);
        Use(B);
        Store.SaveNow();
        Sync(B); // the app could be killed right after this pass, before its own save
        Assert.Contains("remote edit", File.ReadAllText(Path.Combine(B.Dir, "data.json")));
    }

    [Fact]
    public void Headless_run_works_where_the_app_never_made_its_timers()
    {
        var b = Shared();
        Use(B);
        Store.SaveNow();
        Use(A);
        var c = NewList(A, "New on A", "x");
        Await(A.Service.SetSynced(c, true));
        Item(b, "a").Text = "remote";
        Sync(A);

        // a process started only for the background job: no UI timers, the app's normal saver
        UiTimers.Factory = () => throw new InvalidOperationException("No UI timers in this process.");
        Store.Saver = new DebouncedSaveScheduler(Store.SaveNow);
        var result = Await(SyncService.RunHeadless(B.Dir, B.Auth, () => _cloud, "Device B", null, CancellationToken.None));
        Assert.True(result.Changed);
        Assert.Equal(SyncStatus.UpToDate, result.Status);
        var saved = File.ReadAllText(Path.Combine(B.Dir, "data.json"));
        Assert.Contains("remote", saved);
        Assert.Contains("New on A", saved);

        // and the lock was let go: another pass runs
        var again = Await(SyncService.RunHeadless(B.Dir, B.Auth, () => _cloud, "Device B", null, CancellationToken.None));
        Assert.Equal(SyncStatus.UpToDate, again.Status);
    }

    [Fact]
    public void Headless_run_with_the_app_saves_right_away()
    {
        var b = Shared();
        Use(A);
        Item(b, "a").Text = "live";
        Sync(A);
        Use(B);
        Store.SaveNow();
        SyncService.Current = B.Service;
        Await(SyncService.RunHeadless("unused", B.Auth, () => _cloud, "Device B", run => run(), CancellationToken.None));
        Assert.Contains("live", File.ReadAllText(Path.Combine(B.Dir, "data.json")));
    }

    [Fact]
    public void Drive_access_unticked_at_sign_in_asks_to_sign_in_again()
    {
        var b = Shared();
        Use(A);
        _cloud.FailNext.Enqueue(CloudError.Unauthorized); // what DriveCloudStore reports for 403 insufficientPermissions
        Sync(A);
        Assert.True(A.Service.NeedsSignIn);
        Assert.Equal("Google signed you out.", A.Service.StateOf(b).Problem);
    }

    // ================= Deferred review findings =================

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Category_moved_to_another_list_while_edited_elsewhere_leaves_one_copy(bool renameIsNewer)
    {
        SignIn(A);
        SignIn(B);
        var one = NewList(A, "One", "x");
        var two = NewList(A, "Two", "y");
        Await(A.Service.SetSynced(one, true));
        Await(A.Service.SetSynced(two, true));
        Sync(B);
        var cat = one.Categories[0];

        Use(B);
        BoardOf(B, one.Id)!.Categories[0].Name = "renamed on B";
        ChangeTracker.StampChanges(B.Data, _now.AddHours(renameIsNewer ? 1 : -1));
        Sync(B);
        Use(A);
        Actions.MoveCategory(cat, two, 1); // without seeing the rename
        Sync(A);
        Sync(B);
        Sync(A);

        foreach (var d in new[] { A, B })
        {
            Assert.Single(d.Data.Boards.SelectMany(l => l.Categories), c => c.Id == cat.Id);
            Assert.True(d.Data.Boards.SelectMany(l => l.Categories).SelectMany(c => c.Items).Count(i => i.Text == "x") == 1,
                d.Name + ": " + string.Join(" | ", d.Data.Boards.Select(l => l.Title + "[" + string.Join(";", l.Categories.Select(c => c.Name + ":" + string.Join(",", c.Items.Select(i => i.Text)))) + "]")));
        }
    }

    [Fact]
    public void Duplicate_copies_with_the_same_time_keep_the_same_one_everywhere()
    {
        var t = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        AppData Make(bool reversed)
        {
            var data = new AppData();
            foreach (var (list, device) in reversed ? new[] { ("L2", "B"), ("L1", "A") } : new[] { ("L1", "A"), ("L2", "B") })
            {
                var b = new Board { Id = list };
                var c = new Category { Id = "c" + list };
                c.Items.Add(new TodoItem { Id = "x", Text = "from " + device, Sync = new SyncMeta { Changed = new Stamp(device, 5), ModifiedAt = t } });
                b.Categories.Add(c);
                data.Boards.Add(b);
            }
            return data;
        }
        var first = Make(false);
        var second = Make(true);
        SyncService.RemoveDuplicates(first, _ => false);
        SyncService.RemoveDuplicates(second, _ => false);
        string Kept(AppData d) => d.Boards.SelectMany(b => b.Categories).SelectMany(c => c.Items).Single().Text;
        Assert.Equal(Kept(first), Kept(second));
    }

    [Fact]
    public void A_duplicate_being_edited_waits()
    {
        var data = new AppData();
        TodoItem? editing = null;
        foreach (var list in new[] { "L1", "L2" })
        {
            var b = new Board { Id = list };
            var c = new Category();
            var i = new TodoItem { Id = "x", Sync = new SyncMeta { Changed = new Stamp(list, 1), ModifiedAt = DateTime.UtcNow } };
            editing ??= i;
            c.Items.Add(i);
            b.Categories.Add(c);
            data.Boards.Add(b);
        }
        Assert.False(SyncService.RemoveDuplicates(data, o => o == editing));
        Assert.Equal(2, data.Boards.SelectMany(b => b.Categories).SelectMany(c => c.Items).Count());
    }

    [Fact]
    public void Every_minute_timer_waits_for_the_retry()
    {
        var b = Shared();
        Use(A);
        A.Service.Start();
        try
        {
            _cloud.FailNext.Enqueue(CloudError.Offline);
            Await(A.Service.SyncNow());
            Assert.NotNull(A.Service.RetryIn);
            int calls = _cloud.Calls;
            ManualTimer.Created.Single(t => t.Interval == TimeSpan.FromSeconds(20)).Fire();
            Assert.Equal(calls, _cloud.Calls); // the retry timer decides when to try again
            ManualTimer.Created.Single(t => t.Interval == TimeSpan.FromSeconds(15)).Fire();
            Assert.True(_cloud.Calls > calls);
            Assert.Equal(SyncStatus.UpToDate, A.Service.StateOf(b).Status);
        }
        finally { A.Service.Stop(); }
    }

    [Fact]
    public void Turning_off_then_signing_out_leaves_only_the_marker()
    {
        var b = Shared();
        Use(A);
        Await(A.Service.SetSynced(b, false));
        Await(A.Service.SignOut(false));
        Assert.True(_cloud.Copy(FileOf(A, b)).IsMarker);
    }

    [Fact]
    public void Seeing_the_reset_marker_removes_the_own_leftover_files()
    {
        var b = Shared();
        _cloud.Put("reset.json", """{ "device": "other" }""");
        Use(B);
        var mine = FileOf(B, BoardOf(B, b.Id)!);
        Assert.True(_cloud.Has(mine)); // uploaded while the other device deleted the cloud data
        Sync(B);
        Assert.False(_cloud.Has(mine));
        Assert.True(_cloud.Has(FileOf(A, b))); // other devices clean up their own
    }

    [Fact]
    public void A_file_that_cant_be_merged_is_skipped_and_logged()
    {
        var b = Shared();
        // an item in a category the file doesn't have
        var node = System.Text.Json.Nodes.JsonNode.Parse(_cloud.Read(FileOf(A, b)))!;
        node["items"]![0]!["categoryId"] = "nowhere";
        node["seen"]!["A"] = 999999;
        node["items"]![0]!["sync"]!["changed"]!["counter"] = 999999;
        _cloud.Put($"w-{b.Id}.Z.json", node.ToJsonString());
        Use(B);
        Sync(B);
        Assert.Equal(SyncStatus.UpToDate, B.Service.StateOf(BoardOf(B, b.Id)!).Status);
        Assert.Contains("can't be merged", File.ReadAllText(Path.Combine(B.Dir, "log.txt")));
    }
}
