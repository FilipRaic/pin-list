global using System.IO;
using PinList;
using Xunit;

// Store and Actions are static, so tests must not run at the same time
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PinList.Tests;

/// <summary>Saves nothing on its own; counts how often a save was requested.</summary>
sealed class ManualSaver : ISaveScheduler
{
    public int Requests;
    public void Schedule() => Requests++;
    public bool CancelPending() => false;
}

/// <summary>Gives each test an empty data folder and fresh in-memory data.</summary>
public abstract class StoreTestBase : IDisposable
{
    protected readonly string Dir = Path.Combine(Path.GetTempPath(), "PinListTests", Guid.NewGuid().ToString("N"));
    protected readonly List<(string message, bool isError)> Messages = new();
    internal readonly ManualSaver Saver = new();

    // The UI tests' app listens for new widgets (to open their windows). These tests run on another thread
    // without that app, so its listeners are taken off while they run.
    static readonly System.Reflection.FieldInfo BoardAddedField =
        typeof(Actions).GetField(nameof(Actions.BoardAdded), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    readonly object? _appListeners;

    protected StoreTestBase()
    {
        _appListeners = BoardAddedField.GetValue(null);
        BoardAddedField.SetValue(null, null);
        Store.UseDirectory(Dir);
        Store.Saver = Saver;
        Store.Notify = (m, e) => Messages.Add((m, e));
        Actions.ClearUndo();
    }

    public virtual void Dispose()
    {
        BoardAddedField.SetValue(null, _appListeners);
        try { Directory.Delete(Dir, true); } catch { }
    }

    /// <summary>One board with the given categories and items.</summary>
    protected static Board MakeBoard(params (string name, string[] items)[] categories)
    {
        var b = new Board { Title = "Test" };
        foreach (var (name, items) in categories)
        {
            var c = new Category { Name = name };
            foreach (var t in items) c.Items.Add(new TodoItem { Text = t });
            b.Categories.Add(c);
        }
        Store.Data.Boards.Add(b);
        return b;
    }

    protected static string[] Texts(Category c) => c.Items.Select(i => i.Text).ToArray();
}
