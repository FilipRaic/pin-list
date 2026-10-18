using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PinList;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public class TodoItem : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    string _text = "";
    public string Text { get => _text; set => Set(ref _text, value); }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // UI state only, not saved
    bool _isEditing, _isCompleting, _isActive;
    [JsonIgnore] public bool IsEditing { get => _isEditing; set => Set(ref _isEditing, value); }
    [JsonIgnore] public bool IsCompleting { get => _isCompleting; set => Set(ref _isCompleting, value); }
    [JsonIgnore] public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}

public class Category : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    string _name = "New category";
    public string Name { get => _name; set => Set(ref _name, value); }

    string _color = "#60CDFF";
    public string Color { get => _color; set => Set(ref _color, value); }

    bool _collapsed;
    public bool Collapsed { get => _collapsed; set => Set(ref _collapsed, value); }

    public ObservableCollection<TodoItem> Items { get; set; } = new();

    bool _isEditing, _isActive;
    [JsonIgnore] public bool IsEditing { get => _isEditing; set => Set(ref _isEditing, value); }
    [JsonIgnore] public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}

public class Board : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    string _title = "My list";
    public string Title { get => _title; set => Set(ref _title, value); }

    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 480;

    bool _locked;
    public bool Locked { get => _locked; set => Set(ref _locked, value); }

    /// <summary>Key from Themes.BoardStyles (Dark, Light, Yellow, ...).</summary>
    public string Style { get; set; } = "Dark";

    /// <summary>Semi-transparent background (fixed opacity) or solid color.</summary>
    public bool Transparent { get; set; } = true;

    public ObservableCollection<Category> Categories { get; set; } = new();

    bool _isEditingTitle;
    [JsonIgnore] public bool IsEditingTitle { get => _isEditingTitle; set => Set(ref _isEditingTitle, value); }
}

public class ArchivedItem
{
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public string CategoryId { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public string CategoryColor { get; set; } = "#A0A0A0";
    public string BoardId { get; set; } = "";
    public string BoardTitle { get; set; } = "";
}

public class AppData
{
    public int Version { get; set; } = 1;
    public ObservableCollection<Board> Boards { get; set; } = new();
    public ObservableCollection<ArchivedItem> Archive { get; set; } = new();
}
