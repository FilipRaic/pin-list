using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PinList;

/// <summary>List of completed items with the date and time they were completed.</summary>
public partial class ArchiveWindow : Window
{
    /// <summary>The archive entries that match the search, in archive order (newest first).</summary>
    readonly ObservableCollection<ArchivedItem> _shown = new();
    readonly ObservableCollection<ArchivedItem> _archive = Store.Data.Archive;
    readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    public ArchiveWindow()
    {
        InitializeComponent();
        Look.GrayscaleText(this);
        List.ItemsSource = _shown;
        // screen readers read a row as text: "Send the invoice, completed 31.10.2026 14:05, category Work"
        List.ContainerPrepared += (_, e) =>
            Avalonia.Automation.AutomationProperties.SetName(e.Container, e.Container.DataContext?.ToString() ?? "");
        Refilter();

        _archive.CollectionChanged += OnArchiveChanged;
        App.ThemeChanged += StyleTitleBar;
        Closed += (_, _) =>
        {
            _archive.CollectionChanged -= OnArchiveChanged;
            App.ThemeChanged -= StyleTitleBar;
            _statusTimer.Stop();
        };
        Opened += (_, _) => StyleTitleBar();
        _statusTimer.Tick += (_, _) => ShowStatus(null);
        SearchBox.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Refilter(); };
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // Text size: scale the content and make the window bigger to match
        ApplyTextScale();
        double s = A11y.TextScale;
        Height = Math.Min(Height * s, Displays.Primary.Height);
        MinWidth *= s;
        MinHeight *= s;

        Opened += (_, _) =>
        {
            FitWidthToColumns();
            // start in the list (or the search box when it's empty), so the keyboard works right away
            if (_shown.Count > 0) SelectNear(0);
            else SearchBox.Focus();
        };
        UpdateCount();
    }

    internal TextBox Search => SearchBox;
    internal ListBox Items => List;

    void StyleTitleBar()
    {
        var bg = (this.FindResource("ArcBg") as ISolidColorBrush)?.Color ?? Colors.White;
        Native.StyleTitleBar(this, dark: Themes.IsDark(Store.Data.Settings.Theme), bg);
    }

    internal void RefreshAppearance()
    {
        ApplyTextScale();
        StyleTitleBar();
    }

    void ApplyTextScale() =>
        Scaler.LayoutTransform = A11y.TextScale == 1.0 ? null : new ScaleTransform(A11y.TextScale, A11y.TextScale);

    /// <summary>Makes the window exactly wide enough for all columns (no horizontal scrolling).</summary>
    void FitWidthToColumns()
    {
        const double columns = 140 + 320 + 160 + 130 + 100;
        double tableChrome = TableBorder.BorderThickness.Left + TableBorder.BorderThickness.Right
                           + TableBorder.Padding.Left + TableBorder.Padding.Right;
        const double scrollbar = 16; // slim vertical scrollbar + a little slack
        double margins = Root.Margin.Left + Root.Margin.Right;
        double s = A11y.TextScale; // the content is scaled by the text size
        var wa = Displays.Primary;
        Width = Math.Min((columns + tableChrome + scrollbar + margins) * s, wa.Width);
        Position = Displays.ToPixels(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
    }

    void OnArchiveChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Refilter();
        UpdateCount();
    }

    void UpdateCount()
    {
        int n = _archive.Count;
        CountText.Text = n == 1 ? "1 item" : $"{n} items";
        EmptyText.IsVisible = n == 0;
    }

    void Refilter()
    {
        var selected = List.SelectedItems?.Cast<ArchivedItem>().ToList() ?? new();
        _shown.Clear();
        foreach (var a in _archive.Where(Matches)) _shown.Add(a);
        foreach (var a in selected.Where(_shown.Contains)) List.SelectedItems!.Add(a);
    }

    bool Matches(ArchivedItem a)
    {
        var q = (SearchBox.Text ?? "").Trim();
        if (q.Length == 0) return true;
        return a.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || a.CategoryName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || a.BoardTitle.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    void Restore_Click(object? sender, RoutedEventArgs e) => RestoreSelected();

    void Delete_Click(object? sender, RoutedEventArgs e) => DeleteSelected();

    void List_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<ListBoxItem>(includeSelf: true) != null)
            RestoreSelected();
    }

    void List_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
        else if (e.Key == Key.Enter) { RestoreSelected(); e.Handled = true; }
    }

    void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        bool typing = FocusManager?.GetFocusedElement() is TextBox tb && !string.IsNullOrEmpty(tb.Text);
        if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control && !typing)
        {
            A11y.Announce(this, Actions.Undo(out var what) ? "Undone: " + what : "Nothing to undo");
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus(NavigationMethod.Tab);
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = "";
            else Close();
            e.Handled = true;
        }
    }

    /// <summary>Restores the selected items (Ctrl+Z undoes it) and says where items went if their category is gone.</summary>
    void RestoreSelected()
    {
        var selected = List.SelectedItems!.Cast<ArchivedItem>().ToList();
        if (selected.Count == 0) return;
        int index = List.SelectedIndex;

        var restored = Actions.Restore(selected);
        if (restored.Count > 0) SelectNear(index);
        if (restored.Count == 0)
        {
            ShowStatus("There is no category to restore into. Add a category to a widget first.");
            return;
        }

        var moved = restored.Where(r => r.category.Id != r.archived.CategoryId).ToList();
        if (moved.Count == 0)
            ShowStatus(restored.Count == 1 ? "Restored. Ctrl+Z undoes." : $"Restored {restored.Count} items. Ctrl+Z undoes.");
        else if (moved.Count == 1)
            ShowStatus($"\"{moved[0].archived.CategoryName}\" no longer exists, so the item went to \"{moved[0].category.Name}\".");
        else
            ShowStatus($"{moved.Count} items went to \"{moved[0].category.Name}\" because their category no longer exists.");
    }

    /// <summary>How long the status message stays; tests shorten it.</summary>
    internal TimeSpan StatusDuration { get => _statusTimer.Interval; set => _statusTimer.Interval = value; }

    /// <summary>Shows a message in place of the keyboard hint for a few seconds and reads it to screen readers (null hides it).</summary>
    void ShowStatus(string? message)
    {
        _statusTimer.Stop();
        StatusText.Text = message ?? "";
        StatusText.IsVisible = message != null;
        HintText.IsVisible = message == null;
        if (message != null) { _statusTimer.Start(); A11y.Announce(this, message); }
    }

    /// <summary>Permanently deletes the selected items (Ctrl+Z brings them back while the app is running).</summary>
    void DeleteSelected()
    {
        var selected = List.SelectedItems!.Cast<ArchivedItem>().ToList();
        if (selected.Count == 0) return;
        int index = List.SelectedIndex;

        Actions.DeleteArchived(selected);
        A11y.Announce(this, (selected.Count == 1 ? "Deleted 1 item" : $"Deleted {selected.Count} items") + " permanently. Ctrl+Z undoes.");
        SelectNear(index);
    }

    /// <summary>Selects and focuses the row now at this position, so the keyboard can carry on (or the search box if the list is empty).</summary>
    void SelectNear(int index)
    {
        if (_shown.Count == 0)
        {
            SearchBox.Focus();
            return;
        }
        List.SelectedIndex = Math.Clamp(index, 0, _shown.Count - 1);
        Dispatcher.UIThread.Post(() =>
        {
            if (List.ContainerFromIndex(List.SelectedIndex) is ListBoxItem item) item.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);
    }
}
