using System.Text.Json.Nodes;
using PinList;
using Xunit;

namespace PinList.Tests;

public class StoreTests : StoreTestBase
{
    string DataFile => Path.Combine(Dir, "data.json");

    const string Version1File = """
        {
          "version": 1,
          "boards": [ { "id": "b1", "title": "Old", "categories": [ { "id": "c1", "name": "General", "items": [ { "id": "i1", "text": "old item" } ] } ] } ],
          "archive": []
        }
        """;

    [Fact]
    public void First_run_has_no_data() => Assert.False(Store.Load());

    [Fact]
    public void Unknown_fields_survive_load_and_save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(DataFile, """
            { "version": 2, "futureTop": 1,
              "settings": { "theme": "Dark", "futureSetting": "x" },
              "boards": [ { "id": "b", "futureBoard": [1, 2],
                "categories": [ { "id": "c", "futureCat": true,
                  "items": [ { "id": "i", "text": "t", "futureItem": { "a": 1 } } ] } ] } ],
              "archive": [ { "text": "done", "futureArchived": "y" } ] }
            """);
        Assert.True(Store.Load());
        Store.SaveNow();

        var saved = JsonNode.Parse(File.ReadAllText(DataFile))!;
        Assert.Equal(1, (int)saved["futureTop"]!);
        Assert.Equal("x", (string)saved["settings"]!["futureSetting"]!);
        Assert.Equal(2, saved["boards"]![0]!["futureBoard"]!.AsArray().Count);
        Assert.True((bool)saved["boards"]![0]!["categories"]![0]!["futureCat"]!);
        Assert.Equal(1, (int)saved["boards"]![0]!["categories"]![0]!["items"]![0]!["futureItem"]!["a"]!);
        Assert.Equal("y", (string)saved["archive"]![0]!["futureArchived"]!);
    }

    [Fact]
    public void Save_and_load_round_trip()
    {
        var cat = MakeBoard(("Work", new[] { "Write report" })).Categories[0];
        cat.Items[0].DueDate = new DateTime(2030, 3, 4);
        cat.Items[0].ReminderAt = new DateTime(2030, 3, 4, 8, 15, 0);
        Store.Data.Settings.Theme = "Light";
        Store.SaveNow();

        Store.UseDirectory(Dir);
        Assert.True(Store.Load());
        var item = Store.Data.Boards.Single().Categories.Single().Items.Single();
        Assert.Equal("Write report", item.Text);
        Assert.Equal(new DateTime(2030, 3, 4), item.DueDate);
        Assert.Equal(new DateTime(2030, 3, 4, 8, 15, 0), item.ReminderAt);
        Assert.Equal("Light", Store.Data.Settings.Theme);
    }

    [Fact]
    public void Saving_creates_todays_backup()
    {
        MakeBoard(("A", new[] { "a" }));
        Store.SaveNow();
        Assert.True(File.Exists(Path.Combine(Dir, "backups", $"data-{DateTime.Now:yyyy-MM-dd}.json")));
    }

    [Fact]
    public void Only_the_last_seven_backups_are_kept()
    {
        var backups = Path.Combine(Dir, "backups");
        Directory.CreateDirectory(backups);
        for (int i = 1; i <= 10; i++)
            File.WriteAllText(Path.Combine(backups, $"data-2020-01-{i:00}.json"), "{}");

        MakeBoard(("A", new[] { "a" }));
        Store.SaveNow(); // adds today's backup and removes the oldest ones

        var left = Directory.GetFiles(backups, "data-*.json").Select(Path.GetFileName).OrderBy(f => f).ToArray();
        Assert.Equal(7, left.Length);
        Assert.Equal("data-2020-01-05.json", left[0]);
    }

    [Fact]
    public void A_damaged_file_loads_the_latest_backup()
    {
        MakeBoard(("A", new[] { "from backup" }));
        Store.SaveNow(); // also writes today's backup
        File.WriteAllText(DataFile, "{ this is not json");

        Store.UseDirectory(Dir);
        Assert.True(Store.Load());
        Assert.Equal("from backup", Store.Data.Boards.Single().Categories.Single().Items.Single().Text);
        Assert.Contains(Messages, m => m.message.Contains("latest backup"));
        Assert.Single(Directory.GetFiles(Dir, "data.json.corrupt-*"));
    }

    [Fact]
    public void A_damaged_file_without_backup_starts_empty_but_keeps_the_file()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(DataFile, "garbage");

        Assert.True(Store.Load());
        Assert.Empty(Store.Data.Boards);
        Assert.Contains(Messages, m => m.isError);
        Assert.Single(Directory.GetFiles(Dir, "data.json.corrupt-*"));
    }

    [Fact]
    public void A_leftover_temp_file_is_used_when_data_json_is_missing()
    {
        MakeBoard(("A", new[] { "a" }));
        Store.SaveNow();
        File.Move(DataFile, DataFile + ".tmp");

        Store.UseDirectory(Dir);
        Assert.True(Store.Load());
        Assert.Single(Store.Data.Boards);
    }

    [Fact]
    public void A_version_1_file_is_upgraded_and_a_copy_is_kept()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(DataFile, Version1File);

        Assert.True(Store.Load());
        Assert.Equal(AppData.CurrentVersion, Store.Data.Version);
        Assert.Equal("System", Store.Data.Settings.Theme);
        Assert.Equal("old item", Store.Data.Boards[0].Categories[0].Items[0].Text);
        Assert.True(File.Exists(Path.Combine(Dir, "data.v1.json")));
        Assert.Empty(Messages);
    }

    [Fact]
    public void A_file_from_a_newer_version_warns_and_keeps_a_copy()
    {
        Directory.CreateDirectory(Dir);
        var newer = JsonNode.Parse(Version1File)!;
        newer["version"] = AppData.CurrentVersion + 1;
        File.WriteAllText(DataFile, newer.ToJsonString());

        Assert.True(Store.Load());
        Assert.Single(Store.Data.Boards);
        Assert.Contains(Messages, m => m.message.Contains("newer version"));
        Assert.True(File.Exists(Path.Combine(Dir, $"data.v{AppData.CurrentVersion + 1}.json")));
    }

    [Fact]
    public void Parse_rejects_files_that_are_not_pinlist_data()
    {
        Assert.ThrowsAny<Exception>(() => Store.Parse("""{ "name": "something else" }""", out _));
        Assert.ThrowsAny<Exception>(() => Store.Parse("[1, 2, 3]", out _));
    }

    [Fact]
    public void Merge_adds_boards_with_new_ids_and_keeps_archive_links()
    {
        var existing = MakeBoard(("Mine", new[] { "mine" }));
        var imported = Store.Parse(Version1File, out _);
        imported.Archive.Add(new ArchivedItem { Text = "done", CategoryId = "c1", BoardId = "b1", CompletedAt = DateTime.Now });
        imported.Archive.Add(new ArchivedItem { Text = "orphan", CategoryId = "gone", BoardId = "gone", CompletedAt = DateTime.Now.AddDays(-1) });

        var added = Assert.Single(Store.Merge(imported));

        Assert.Equal(2, Store.Data.Boards.Count);
        Assert.Same(existing, Store.Data.Boards[0]);
        Assert.NotEqual("b1", added.Id);
        Assert.NotEqual("c1", added.Categories[0].Id);

        var done = Store.Data.Archive.Single(a => a.Text == "done");
        Assert.Equal(added.Categories[0].Id, done.CategoryId);
        Assert.Equal(added.Id, done.BoardId);
        Assert.Equal("", Store.Data.Archive.Single(a => a.Text == "orphan").CategoryId);
        Assert.Equal("done", Store.Data.Archive[0].Text); // newest first
    }

    [Fact]
    public void ReplaceAll_keeps_a_safety_copy_and_the_theme_setting()
    {
        MakeBoard(("Mine", new[] { "mine" }));
        Store.Data.Settings.Theme = "Dark";

        var safety = Store.ReplaceAll(Store.Parse(Version1File, out _));

        Assert.Equal("Old", Store.Data.Boards.Single().Title);
        Assert.Equal("Dark", Store.Data.Settings.Theme);
        Assert.Contains("\"mine\"", File.ReadAllText(safety));
        Assert.Contains("\"Old\"", File.ReadAllText(DataFile));
    }

    [Fact]
    public void Export_writes_a_file_that_can_be_imported()
    {
        MakeBoard(("A", new[] { "exported" }));
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, "export.json");

        Store.Export(path);

        var back = Store.ReadFile(path, out int version);
        Assert.Equal(AppData.CurrentVersion, version);
        Assert.Equal("exported", back.Boards[0].Categories[0].Items[0].Text);
    }

    [Fact]
    public void The_log_is_rotated_when_it_gets_big()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, "log.txt"), new string('x', 1024 * 1024 + 10));

        Store.Log("new entry");

        Assert.True(File.Exists(Path.Combine(Dir, "log.old.txt")));
        Assert.Contains("new entry", File.ReadAllText(Path.Combine(Dir, "log.txt")));
        Assert.True(new FileInfo(Path.Combine(Dir, "log.txt")).Length < 1000);
    }
}
