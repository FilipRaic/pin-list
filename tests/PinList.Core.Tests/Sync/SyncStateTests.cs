using PinList;
using PinList.Sync;
using Xunit;

namespace PinList.Tests.Sync;

public class SyncStateTests : StoreTestBase
{
    string StatePath => Path.Combine(Dir, "sync-state.json");

    [Fact]
    public void Missing_file_is_empty_state()
    {
        var s = SyncState.Load(Dir);
        Assert.Null(s.Account);
        Assert.Null(s.DeviceId);
        Assert.Equal(0, s.Counter);
        Assert.Empty(s.MergedFiles);
        Assert.Empty(s.TurningOff);
        Assert.Null(s.LastSyncUtc);
    }

    [Fact]
    public void Round_trips()
    {
        var merged = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        var s = new SyncState
        {
            Account = new GoogleAccount("123", "me@example.com"), DeviceId = "dev", Counter = 77,
            LastSyncUtc = merged.AddMinutes(5),
        };
        s.MergedFiles["w-L.B.json"] = merged;
        s.TurningOff.Add("L");
        s.Save(Dir);

        Assert.False(File.Exists(StatePath + ".tmp"));
        var back = SyncState.Load(Dir);
        Assert.Equal(s.Account, back.Account);
        Assert.Equal(("dev", 77L), (back.DeviceId, back.Counter));
        Assert.Equal(merged, back.MergedFiles["w-L.B.json"]);
        Assert.Equal(DateTimeKind.Utc, back.MergedFiles["w-L.B.json"].Kind);
        Assert.Equal(new[] { "L" }, back.TurningOff);
        Assert.Equal(merged.AddMinutes(5), back.LastSyncUtc);

        s.Counter = 78;
        s.Save(Dir); // replaces the existing file
        Assert.Equal(78, SyncState.Load(Dir).Counter);
    }

    [Fact]
    public void Broken_file_is_empty_state_and_logged()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(StatePath, "{ not json");
        Assert.Equal(0, SyncState.Load(Dir).Counter);
        Assert.Contains("sync-state.json", File.ReadAllText(Path.Combine(Dir, "log.txt")));
    }

    [Fact]
    public void Counter_uses_the_higher_of_data_and_state()
    {
        // data.json restored from an older backup: the state remembers the higher counter
        var data = new AppData { SyncInfo = new DeviceInfo { DeviceId = "dev", Counter = 10 } };
        var state = new SyncState { DeviceId = "dev", Counter = 50 };
        SyncState.ReconcileCounter(data, state);
        Assert.Equal((50L, 50L), (data.SyncInfo.Counter, state.Counter));

        // sync-state.json lost or older (killed between the two writes)
        data.SyncInfo.Counter = 60;
        SyncState.ReconcileCounter(data, state);
        Assert.Equal((60L, 60L), (data.SyncInfo.Counter, state.Counter));
    }

    [Fact]
    public void First_reconcile_takes_the_device_from_the_data()
    {
        var data = new AppData { SyncInfo = new DeviceInfo { DeviceId = "dev", Counter = 5 } };
        var state = new SyncState();
        SyncState.ReconcileCounter(data, state);
        Assert.Equal(("dev", 5L), (state.DeviceId, state.Counter));

        var empty = new AppData();
        SyncState.ReconcileCounter(empty, new SyncState());
        Assert.Null(empty.SyncInfo); // nothing to do before anything was stamped
    }

    [Fact]
    public void Data_without_device_info_keeps_this_devices_id()
    {
        var data = new AppData(); // e.g. replaced by an import
        SyncState.ReconcileCounter(data, new SyncState { DeviceId = "dev", Counter = 9 });
        Assert.Equal(("dev", 9L), (data.SyncInfo!.DeviceId, data.SyncInfo.Counter));
    }

    [Fact]
    public void Data_copied_from_another_device_gets_a_new_device_id()
    {
        var data = new AppData { SyncInfo = new DeviceInfo { DeviceId = "other", Counter = 500 } };
        var state = new SyncState { DeviceId = "mine", Counter = 9 };
        SyncState.ReconcileCounter(data, state);
        Assert.NotEqual("other", data.SyncInfo.DeviceId);
        Assert.NotEqual("mine", data.SyncInfo.DeviceId);
        Assert.Equal(data.SyncInfo.DeviceId, state.DeviceId);
        Assert.Equal(data.SyncInfo.Counter, state.Counter);
    }

    [Fact]
    public void Loading_data_uses_the_counter_from_the_state()
    {
        MakeBoard(("Work", new[] { "a" }));
        Store.SaveNow();
        var id = Store.Data.SyncInfo!.DeviceId;
        new SyncState { DeviceId = id, Counter = 1000 }.Save(Dir);

        File.WriteAllText(Path.Combine(Dir, "data.json"),
            File.ReadAllText(Path.Combine(Dir, "data.json")).Replace("\"text\": \"a\"", "\"text\": \"b\""));
        Store.UseDirectory(Dir);
        Store.Load();
        var item = Store.AllItems().Single();
        Assert.Equal(1001, item.Sync!.Changed!.Counter); // stamped after the counter went up, never reused
    }

    [Fact]
    public void Missing_parts_load_as_empty()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(StatePath, """{ "mergedFiles": null, "turningOff": null, "stripping": null, "deletedLists": null, "uploaded": null, "markers": null }""");
        var s = SyncState.Load(Dir);
        Assert.Empty(s.MergedFiles);
        Assert.Empty(s.TurningOff);
        Assert.Empty(s.Stripping);
        Assert.Empty(s.DeletedLists);
        Assert.Empty(s.Uploaded);
        Assert.Empty(s.Markers);
        File.WriteAllText(StatePath, "null");
        Assert.Null(SyncState.Load(Dir).Account);
    }
}
