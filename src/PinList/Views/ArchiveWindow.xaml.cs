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
        Closed += (_, _) => Store.Data.Archive.CollectionChanged -= OnArchiveChanged;
        SourceInitialized += (_, _) => StyleTitleBar();
        Loaded += (_, _) => FitWidthToColumns();
        UpdateCount();
    }

    void StyleTitleBar()
    {
        var bg = (TryFindResource("ArcBg") as SolidColorBrush)?.Color ?? Colors.White;
        Native.StyleTitleBar(this, dark: true, bg);
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
        double content = columns + tableChrome + scrollbar + margins;
        double windowChrome = ActualWidth - (Root.ActualWidth + margins); // window frame

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

    void Search_TextChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();

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
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && !typing)
        {
            Actions.Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
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

    void RestoreSelected()
    {
        var selected = List.SelectedItems.Cast<ArchivedItem>().ToList();
        foreach (var a in selected)
            if (!Actions.Restore(a)) break; // no category left to restore into
    }

    /// <summary>Permanently deletes the selected items (Ctrl+Z brings them back while the app is running).</summary>
    void DeleteSelected()
    {
        var selected = List.SelectedItems.Cast<ArchivedItem>().ToList();
        if (selected.Count == 0) return;
        int index = List.SelectedIndex;

        Actions.DeleteArchived(selected);

        // select the next row so Delete can be pressed again
        if (List.Items.Count > 0)
        {
            List.SelectedIndex = Math.Min(index, List.Items.Count - 1);
            if (List.ItemContainerGenerator.ContainerFromIndex(List.SelectedIndex) is ListViewItem item)
                item.Focus();
        }
    }
}
