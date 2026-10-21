using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace PinList;

/// <summary>List of completed items with the date and time they were completed.</summary>
public partial class ArchiveWindow : Window
{
    readonly ICollectionView _view;

    public ArchiveWindow()
    {
        InitializeComponent();
        _view = new CollectionViewSource { Source = Store.Data.Archive }.View;
        _view.Filter = Matches;
        List.ItemsSource = _view;

        Store.Data.Archive.CollectionChanged += OnArchiveChanged;
        var archive = Store.Data.Archive;
        App.ThemeChanged += StyleTitleBar;
        Closed += (_, _) =>
        {
            archive.CollectionChanged -= OnArchiveChanged;
            App.ThemeChanged -= StyleTitleBar;
            _statusTimer.Stop();
        };
        SourceInitialized += (_, _) => StyleTitleBar();
        _statusTimer.Tick += (_, _) => ShowStatus(null);

        // Windows text size: scale the content and make the window bigger to match
        A11y.ApplyTextScale(this);
        double s = A11y.TextScale;
        Height = Math.Min(Height * s, SystemParameters.WorkArea.Height);
        MinWidth *= s;
        MinHeight *= s;

        Loaded += (_, _) =>
        {
            FitWidthToColumns();
            // start in the list (or the search box when it's empty), so the keyboard works right away
            if (List.Items.Count > 0)
            {
                List.SelectedIndex = 0;
                (List.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem)?.Focus();
            }
            else SearchBox.Focus();
        };
        UpdateCount();
    }

    void StyleTitleBar()
    {
        var bg = (TryFindResource("ArcBg") as SolidColorBrush)?.Color ?? Colors.White;
        Native.StyleTitleBar(this, dark: Themes.IsDark(Store.Data.Settings.Theme), bg);
    }

    /// <summary>
    /// Makes the window exactly wide enough for all columns (no horizontal scrolling).
    /// If the user widens a column, a horizontal scrollbar appears.
    /// </summary>
    void FitWidthToColumns()
    {
        double columns = ColumnsView.Columns.Sum(c => c.Width);
        double tableChrome = TableBorder.BorderThickness.Left + TableBorder.BorderThickness.Right
                           + TableBorder.Padding.Left + TableBorder.Padding.Right;
        const double scrollbar = 16; // slim vertical scrollbar (6 + margins) + ListView border + a little slack
        double margins = Root.Margin.Left + Root.Margin.Right;
        double s = A11y.TextScale; // the content is scaled by the Windows text size, the margins aren't
        double content = (columns + tableChrome + scrollbar) * s + margins;
        double windowChrome = ActualWidth - (Root.ActualWidth * s + margins); // window frame

        var wa = SystemParameters.WorkArea;
        Width = Math.Min(content + windowChrome, wa.Width);
        Left = wa.Left + (wa.Width - Width) / 2;
    }

    void OnArchiveChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateCount();

    void UpdateCount()
    {
        int n = Store.Data.Archive.Count;
        CountText.Text = n == 1 ? "1 item" : $"{n} items";
        EmptyText.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    bool Matches(object o)
    {
        var q = SearchBox.Text.Trim();
        if (q.Length == 0) return true;
        var a = (ArchivedItem)o;
        return a.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || a.CategoryName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || a.BoardTitle.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    void Search_TextChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    void Restore_Click(object sender, RoutedEventArgs e) => RestoreSelected();

    void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(List, (DependencyObject)e.OriginalSource) is ListViewItem)
            RestoreSelected();
    }

    void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
        else if (e.Key == Key.Enter) { RestoreSelected(); e.Handled = true; }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool typing = Keyboard.FocusedElement is TextBox tb && tb.Text.Length > 0;
        if (e.Key == Key.Z && InputState.Modifiers() == ModifierKeys.Control && !typing)
        {
            A11y.Announce(this, Actions.Undo(out var what) ? "Undone: " + what : "Nothing to undo");
            e.Handled = true;
        }
        else if (e.Key == Key.F && InputState.Modifiers() == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (SearchBox.Text.Length > 0) SearchBox.Clear();
            else Close();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    /// <summary>Restores the selected items (Ctrl+Z undoes it) and says where items went if their category is gone.</summary>
    void RestoreSelected()
    {
        var selected = List.SelectedItems.Cast<ArchivedItem>().ToList();
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

    readonly System.Windows.Threading.DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    /// <summary>Shows a message in place of the keyboard hint for a few seconds and reads it to screen readers (null hides it).</summary>
    void ShowStatus(string? message)
    {
        _statusTimer.Stop();
        StatusText.Text = message ?? "";
        StatusText.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
        HintText.Visibility = message == null ? Visibility.Visible : Visibility.Collapsed;
        if (message != null) { _statusTimer.Start(); A11y.Announce(this, message); }
    }

    /// <summary>Permanently deletes the selected items (Ctrl+Z brings them back while the app is running).</summary>
    void DeleteSelected()
    {
        var selected = List.SelectedItems.Cast<ArchivedItem>().ToList();
        if (selected.Count == 0) return;
        int index = List.SelectedIndex;

        Actions.DeleteArchived(selected);
        A11y.Announce(this, (selected.Count == 1 ? "Deleted 1 item" : $"Deleted {selected.Count} items") + " permanently. Ctrl+Z undoes.");
        SelectNear(index);
    }

    /// <summary>Selects and focuses the row now at this position, so the keyboard can carry on (or the search box if the list is empty).</summary>
    void SelectNear(int index)
    {
        if (List.Items.Count == 0)
        {
            SearchBox.Focus();
            return;
        }
        List.SelectedIndex = Math.Clamp(index, 0, List.Items.Count - 1);
        List.UpdateLayout();
        if (List.ItemContainerGenerator.ContainerFromIndex(List.SelectedIndex) is ListViewItem item)
            item.Focus();
    }
}
