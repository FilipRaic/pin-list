using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>Saving and loading when things go wrong, and the smaller Store helpers.</summary>
public class MoreStoreTests : StoreTestBase
{
    string DataFile => Path.Combine(Dir, "data.json");

    sealed class PendingSaver : ISaveScheduler
    {
        public bool Pending = true;
        public void Schedule() => Pending = true;
        public bool CancelPending() { var p = Pending; Pending = false; return p; }
    }

    [Fact]
    public void Flush_saves_only_when_a_save_is_pending()
    {
        var saver = new PendingSaver();
        Store.Saver = saver;
        MakeBoard(("A", new[] { "a" }));

        Store.Flush();
        Assert.True(File.Exists(DataFile));

        File.Delete(DataFile);
        Store.Flush(); // nothing pending any more
        Assert.False(File.Exists(DataFile));
    }

    [Fact]
    public void A_failing_save_is_retried_five_times_and_logged()
    {
        // a file where the data folder should be: every save fails
        Directory.CreateDirectory(Dir);
        var blocked = Path.Combine(Dir, "blocked");
        File.WriteAllText(blocked, "");
        Store.UseDirectory(blocked);

        for (int i = 0; i < 7; i++) Store.SaveNow();

        Assert.Equal(5, Saver.Requests); // retries stop after 5 failures
    }

    [Fact]
    public void A_successful_save_resets_the_retry_count()
    {
        Directory.CreateDirectory(Dir);
        var blocked = Path.Combine(Dir, "blocked");
        File.WriteAllText(blocked, "");
        Store.UseDirectory(blocked);
        Store.SaveNow();
        Assert.Equal(1, Saver.Requests);

        Store.UseDirectory(Dir);
        Store.SaveNow(); // works
        Assert.True(File.Exists(DataFile));
    }

    [Fact]
    public void Backup_and_log_problems_never_stop_saving()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, "backups"), "a file where the backups folder should be");
        MakeBoard(("A", new[] { "a" }));

        Store.SaveNow();

        Assert.True(File.Exists(DataFile));
        Assert.Contains("Backup:", File.ReadAllText(Path.Combine(Dir, "log.txt")));
    }

    [Fact]
    public void Logging_into_an_unusable_folder_is_silently_skipped()
    {
        Directory.CreateDirectory(Dir);
        var blocked = Path.Combine(Dir, "blocked");
        File.WriteAllText(blocked, "");
        Store.UseDirectory(blocked);

        Store.Log("nowhere to go"); // must not throw
    }

    [Fact]
    public void A_damaged_file_with_a_damaged_backup_starts_empty()
    {
        Directory.CreateDirectory(Path.Combine(Dir, "backups"));
        File.WriteAllText(Path.Combine(Dir, "backups", "data-2020-01-01.json"), "also damaged");
        File.WriteAllText(DataFile, "damaged");

        Assert.True(Store.Load());

        Assert.Empty(Store.Data.Boards);
        Assert.Contains(Messages, m => m.isError);
        Assert.Contains("Backup could not be read either", File.ReadAllText(Path.Combine(Dir, "log.txt")));
    }

    [Fact]
    public void Upgrading_still_works_when_the_copy_cannot_be_made()
    {
        Directory.CreateDirectory(Path.Combine(Dir, "data.v1.json", "in the way")); // a folder blocks the copy
        File.WriteAllText(DataFile, """{ "version": 1, "boards": [], "archive": [] }""");

        Assert.True(Store.Load());
        Assert.Contains("Could not keep a copy", File.ReadAllText(Path.Combine(Dir, "log.txt")));
    }

    [Fact]
    public void Missing_parts_of_a_file_get_defaults()
    {
        var data = Store.Parse("""{ "boards": [], "settings": null, "archive": null }""", out int version);
        Assert.Equal(1, version); // no version number = the first format
        Assert.NotNull(data.Settings);
        Assert.NotNull(data.Archive);
        Assert.Equal(AppData.CurrentVersion, data.Version);
    }

    [Fact]
    public void A_version_0_file_is_treated_like_version_1()
    {
        var data = Store.Parse("""{ "version": 0, "boards": [] }""", out int version);
        Assert.Equal(0, version);
        Assert.Equal(AppData.CurrentVersion, data.Version);
    }

    [Fact]
    public void Default_board_sits_in_the_top_right_corner()
    {
        var b = Store.DefaultBoard(new WorkArea(0, 0, 1920, 1040), "Light");
        Assert.Equal((1920 - 320 - 24, 24.0), (b.X, b.Y));
        Assert.Equal("Light", b.Style);
        Assert.Equal("General", Assert.Single(b.Categories).Name);
    }

    [Fact]
    public void AllItems_lists_items_of_every_board()
    {
        MakeBoard(("A", new[] { "a", "b" }));
        MakeBoard(("B", new[] { "c" }));
        Assert.Equal(new[] { "a", "b", "c" }, Store.AllItems().Select(i => i.Text));
        Assert.Equal("B", Store.FindCategory(Store.Data.Boards[1].Categories[0].Id)!.Name);
        Assert.Null(Store.FindCategory("nope"));
        Assert.Equal("A", Store.FirstCategory()!.Name);
    }
}

public class ModelTests
{
    [Fact]
    public void Due_flags_follow_the_date()
    {
        var item = new TodoItem();
        Assert.False(item.HasDue);
        Assert.Equal("", item.DueText);
        Assert.Equal("", item.ReminderText);

        item.DueDate = DateTime.Today;
        Assert.True(item.HasDue && item.IsDueToday && !item.IsOverdue);

        item.DueDate = DateTime.Today.AddDays(-1);
        Assert.True(item.IsOverdue && !item.IsDueToday);
    }

    [Fact]
    public void UI_state_raises_change_notifications()
    {
        var changed = new List<string?>();
        var cat = new Category();
        var board = new Board();
        cat.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        board.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        cat.IsEditing = true;
        cat.IsActive = true;
        board.IsEditingTitle = true;
        board.Locked = true;
        cat.Color = "#000000";

        Assert.Equal(new[] { "IsEditing", "IsActive", "IsEditingTitle", "Locked", "Color" }, changed);
    }

    [Fact]
    public void Archived_item_reads_as_one_line()
    {
        var a = new ArchivedItem { Text = "a\nb", CategoryName = "Work", CompletedAt = new DateTime(2026, 1, 2, 3, 4, 0) };
        Assert.StartsWith("a, b, completed ", a.ToString());
        Assert.EndsWith("category Work", a.ToString());
    }
}
