using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media;

namespace PinList;

/// <summary>One line of a card's preview: a category (colored dot) or an item (empty circle).</summary>
public record PreviewLine(bool IsCategory, string Text, IBrush Dot);

/// <summary>
/// A board as a card on the phone's first screen: its color, title, counts and a short preview. Keeps itself up to
/// date while items are added, changed, moved or completed.
/// </summary>
public sealed class BoardCard : Observable, IDisposable
{
    /// <summary>How many lines (categories and items) the preview shows.</summary>
    public const int PreviewLines = 4;

    public Board Board { get; }

    public BoardCard(Board board)
    {
        Board = board;
        board.PropertyChanged += OnBoardChanged;
        Refresh();
    }

    BoardStyle Style => Themes.Get(Board.Style);
    public IBrush Background => Themes.Brush(Style.Background);
    public IBrush Foreground => Themes.Brush(Style.IsDark ? "#F2FFFFFF" : "#E6000000");
    public IBrush Muted => Themes.Brush(Style.IsDark ? "#8CFFFFFF" : "#96000000");
    public IBrush Overdue => Themes.Brush(Style.IsDark ? "#FF99A4" : "#B3261E");
    public IBrush Border => Themes.Brush(Style.IsDark ? "#26FFFFFF" : "#1F000000");

    public bool HasTitle => !string.IsNullOrWhiteSpace(Board.Title);
    public string DisplayTitle => HasTitle ? Board.Title : "No title";

    public ObservableCollection<PreviewLine> Preview { get; } = new();

    int _openCount, _overdueCount;
    string _moreText = "";
    public int OpenCount { get => _openCount; private set => Set(ref _openCount, value); }
    public int OverdueCount { get => _overdueCount; private set { if (Set(ref _overdueCount, value)) { Raise(nameof(HasOverdue)); Raise(nameof(OverdueText)); } } }
    public string MoreText { get => _moreText; private set { if (Set(ref _moreText, value)) Raise(nameof(HasMore)); } }
    public bool HasOverdue => OverdueCount > 0;
    public string OverdueText => $"{OverdueCount} overdue";
    public bool HasMore => MoreText.Length > 0;

    /// <summary>What a screen reader says for the card, e.g. "Work, 5 items, 1 overdue".</summary>
    public string AccessibleName =>
        $"{DisplayTitle}, {(OpenCount == 1 ? "1 item" : $"{OpenCount} items")}{(HasOverdue ? $", {OverdueText}" : "")}";

    readonly List<Category> _watchedCategories = new();
    readonly List<TodoItem> _watchedItems = new();

    /// <summary>Rebuilds the preview and counts, and watches the categories and items shown now.</summary>
    public void Refresh()
    {
        Unwatch();
        Board.Categories.CollectionChanged += OnListChanged;
        foreach (var c in Board.Categories)
        {
            _watchedCategories.Add(c);
            c.Items.CollectionChanged += OnListChanged;
            c.PropertyChanged += OnChildChanged;
            foreach (var i in c.Items)
            {
                _watchedItems.Add(i);
                i.PropertyChanged += OnChildChanged;
            }
        }

        var items = Board.Categories.SelectMany(c => c.Items).ToList();
        OpenCount = items.Count;
        OverdueCount = items.Count(i => i.IsOverdue);

        Preview.Clear();
        int shown = 0;
        foreach (var c in Board.Categories)
        {
            if (Preview.Count >= PreviewLines) break;
            Preview.Add(new PreviewLine(true, c.Name, Themes.Brush(c.Color)));
            foreach (var i in c.Items)
            {
                if (Preview.Count >= PreviewLines) break;
                Preview.Add(new PreviewLine(false, FirstLine(i.Text), Brushes.Transparent));
                shown++;
            }
        }
        MoreText = items.Count > shown ? $"+{items.Count - shown} more" : "";
        Raise(nameof(AccessibleName));
    }

    static string FirstLine(string s)
    {
        int i = s.IndexOf('\n');
        return i < 0 ? s : s[..i];
    }

    void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    void OnChildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TodoItem.Text) or nameof(TodoItem.DueDate) or nameof(TodoItem.IsOverdue)
            or nameof(Category.Name) or nameof(Category.Color))
            Refresh();
    }

    void OnBoardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Board.Title)) return;
        Raise(nameof(HasTitle));
        Raise(nameof(DisplayTitle));
        Raise(nameof(AccessibleName));
    }

    /// <summary>The board's color changed (Style isn't observable, so the screen calls this).</summary>
    public void RefreshColors()
    {
        foreach (var p in new[] { nameof(Background), nameof(Foreground), nameof(Muted), nameof(Overdue), nameof(Border) })
            Raise(p);
    }

    void Unwatch()
    {
        Board.Categories.CollectionChanged -= OnListChanged;
        foreach (var c in _watchedCategories)
        {
            c.Items.CollectionChanged -= OnListChanged;
            c.PropertyChanged -= OnChildChanged;
        }
        foreach (var i in _watchedItems) i.PropertyChanged -= OnChildChanged;
        _watchedCategories.Clear();
        _watchedItems.Clear();
    }

    public void Dispose()
    {
        Unwatch();
        Board.PropertyChanged -= OnBoardChanged;
    }
}
