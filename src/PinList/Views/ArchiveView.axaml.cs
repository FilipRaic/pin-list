using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PinList;

/// <summary>
/// The archive section of the PinList window: completed items grouped by day (Today, Yesterday, dates), with a
/// search box. Restore and Delete show on the hovered row and the selected rows. Day headings are rows of the list too,
/// but they can't be selected or focused.
/// </summary>
public partial class ArchiveView : UserControl
{
    /// <summary>Day headings and the archive entries that match the search, newest first.</summary>
    readonly ObservableCollection<object> _rows = new();
    ObservableCollection<ArchivedItem> _archive = Store.Data.Archive;
    readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    bool _fixingSelection;

    public ArchiveView()
    {
        InitializeComponent();
        Look.GrayscaleText(this);
        HintText.Text = $"Double-click restores  ·  {OsText.Undo} undoes  ·  {OsText.Find} searches";
        AutomationProperties.SetHelpText(List,
            $"{OsText.EnterName} restores, {OsText.DeleteName} deletes permanently, {OsText.Undo} undoes");
        List.ItemTemplate = ArchiveRowTemplate.Instance;
        List.ItemsSource = _rows;
        // screen readers read a row as text: "Send the invoice, completed 31.10.2026 14:05, list Home, category Work, added 5 Jan"
        List.ContainerPrepared += (_, e) =>
        {
            var data = e.Container.DataContext;
            bool header = data is ArchiveDayHeader;
            e.Container.Focusable = !header;
            e.Container.IsHitTestVisible = !header;
            AutomationProperties.SetName(e.Container, data is ArchiveDayHeader h ? h.Title : data!.ToString());
        };
        List.SelectionChanged += (_, _) =>
        {
            KeepHeadersOutOfTheSelection();
            UpdateActionButtons();
        };
        List.AddHandler(Button.ClickEvent, RowButton_Click);
        List.AddHandler(KeyDownEvent, List_Navigate, RoutingStrategies.Tunnel);

        Refilter();
        _archive.CollectionChanged += OnArchiveChanged;
        _statusTimer.Tick += (_, _) => ShowStatus(null);
        SearchBox.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Refilter(); };
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        DetachedFromVisualTree += (_, _) => _statusTimer.Stop();
        UpdateCount();
    }

    internal TextBox Search => SearchBox;
    internal ListBox Items => List;

    /// <summary>The archive entries that are shown (headings left out), in the order of the list.</summary>
    internal ArchivedItem[] Shown => _rows.OfType<ArchivedItem>().ToArray();

    /// <summary>The archive was replaced (an import): show the new one.</summary>
    internal void Reload()
    {
        _archive.CollectionChanged -= OnArchiveChanged;
        _archive = Store.Data.Archive;
        _archive.CollectionChanged += OnArchiveChanged;
        Refilter();
        UpdateCount();
    }

    /// <summary>
    /// Puts the keyboard in the list (or the search box when it is empty), so the keyboard works right away.
    /// Called when the section is shown.
    /// </summary>
    internal void FocusStart()
    {
        if (Shown.Length > 0) SelectNear(0);
        else Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Loaded);
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
        var selected = List.SelectedItems!.OfType<ArchivedItem>().ToList();
        var rows = ArchiveGroups.Rows(_archive.Where(Matches), DateTime.Now);
        _fixingSelection = true;
        _rows.Clear();
        foreach (var row in rows) _rows.Add(row);
        _fixingSelection = false;
        foreach (var a in selected.Where(_rows.Contains)) List.SelectedItems!.Add(a);
    }

    bool Matches(ArchivedItem a)
    {
        var q = (SearchBox.Text ?? "").Trim();
        if (q.Length == 0) return true;
        return a.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || a.CategoryName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || a.BoardTitle.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Select all, Shift+arrows and clicks can include a day heading: take it out again.</summary>
    void KeepHeadersOutOfTheSelection()
    {
        if (_fixingSelection || List.SelectedItems is not { } selected) return;
        var headers = selected.OfType<ArchiveDayHeader>().ToList();
        if (headers.Count == 0) return;
        _fixingSelection = true;
        foreach (var header in headers) selected.Remove(header);
        _fixingSelection = false;
    }

    // ================= Buttons on a row =================

    void RowButton_Click(object? sender, RoutedEventArgs e)
    {
        // the only buttons in the list are the Restore and Delete buttons of the rows
        var button = (Button)e.Source!;
        var item = (ArchivedItem)button.DataContext!;
        bool restore = button.Classes.Contains("restore");
        // the buttons of a selected row act on the whole selection, the buttons of another row only on that row
        if (!List.SelectedItems!.Contains(item))
        {
            List.SelectedItems.Clear();
            List.SelectedItems.Add(item);
        }
        if (restore) RestoreSelected();
        else DeleteSelected();
        e.Handled = true;
    }

    void RestoreButton_Click(object? sender, RoutedEventArgs e) => RestoreSelected();

    void DeleteButton_Click(object? sender, RoutedEventArgs e) => DeleteSelected();

    /// <summary>The Restore and Delete buttons under the list work only when entries are selected.</summary>
    void UpdateActionButtons()
    {
        bool any = List.SelectedItems!.OfType<ArchivedItem>().Any();
        RestoreButton.IsEnabled = any;
        DeleteButton.IsEnabled = any;
    }

    void List_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<ListBoxItem>(includeSelf: true) != null && v.FindAncestorOfType<Button>(includeSelf: true) == null)
            RestoreSelected();
    }

    /// <summary>Up and Down go from entry to entry, never onto a day heading (this runs before the list's own handling).</summary>
    void List_Navigate(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) || e.KeyModifiers != KeyModifiers.None) return;
        var shown = Shown;
        if (shown.Length > 0)
        {
            int now = SelectedOrdinal();
            SelectNear(e.Key == Key.Up ? Math.Max(0, now - 1) : Math.Min(shown.Length - 1, now + 1));
        }
        e.Handled = true;
    }

    void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var focused = TopLevel.GetTopLevel(this)!.FocusManager?.GetFocusedElement();
        bool typing = focused is TextBox tb && !string.IsNullOrEmpty(tb.Text);
        bool inText = focused is TextBox;
        bool anySelected = List.SelectedItems!.OfType<ArchivedItem>().Any();
        if (e.Key == Key.Z && OsText.IsCommand(e.KeyModifiers) && !typing)
        {
            A11y.Announce(this, Actions.Undo(out var what) ? "Undone: " + what : "Nothing to undo");
            e.Handled = true;
        }
        // Enter and Delete act on the selection wherever the keyboard is in the archive, except in the search box.
        // This runs before the list, which would otherwise take Enter for itself.
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && !inText && anySelected && focused is not Button)
        {
            RestoreSelected();
            e.Handled = true;
        }
        else if (OsText.IsDeleteKey(e.Key) && e.KeyModifiers == KeyModifiers.None && !inText && anySelected)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.F && OsText.IsCommand(e.KeyModifiers))
        {
            SearchBox.Focus(NavigationMethod.Tab);
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = "";
            else ((Window)TopLevel.GetTopLevel(this)!).Close(); // the PinList window only hides
            e.Handled = true;
        }
    }

    // ================= Restore and delete =================

    /// <summary>Restores the selected items (Ctrl+Z undoes it) and says where items went if their category is gone.</summary>
    void RestoreSelected()
    {
        var selected = List.SelectedItems!.OfType<ArchivedItem>().ToList();
        if (selected.Count == 0) return;
        int index = SelectedOrdinal();

        var restored = Actions.Restore(selected);
        if (restored.Count > 0) SelectNear(index);
        if (restored.Count == 0)
        {
            ShowStatus("There is no category to restore into. Add a category to a widget first.");
            return;
        }

        var moved = restored.Where(r => r.category.Id != r.archived.CategoryId).ToList();
        if (moved.Count == 0)
            ShowStatus(restored.Count == 1 ? $"Restored. {OsText.Undo} undoes." : $"Restored {restored.Count} items. {OsText.Undo} undoes.");
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
    async void DeleteSelected()
    {
        var selected = List.SelectedItems!.OfType<ArchivedItem>().ToList();
        if (selected.Count == 0) return;
        int index = SelectedOrdinal();
        bool one = selected.Count == 1;
        if (!await Dialogs.Ask(TopLevel.GetTopLevel(this) as Window, one ? "Delete 1 item permanently?" : $"Delete {selected.Count} items permanently?",
                $"{OsText.Undo} brings {(one ? "it" : "them")} back while PinList is running.", "Delete"))
            return;

        Actions.DeleteArchived(selected);
        A11y.Announce(this, (selected.Count == 1 ? "Deleted 1 item" : $"Deleted {selected.Count} items") + $" permanently. {OsText.Undo} undoes.");
        SelectNear(index);
    }

    // ================= Selection =================

    /// <summary>The position of the first selected entry among the entries (day headings don't count).</summary>
    int SelectedOrdinal()
    {
        var shown = Shown;
        var first = List.SelectedItems!.OfType<ArchivedItem>().Select(a => Array.IndexOf(shown, a)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        return first;
    }

    /// <summary>Selects and focuses the entry now at this position, so the keyboard can carry on (or the search box if the list is empty).</summary>
    void SelectNear(int ordinal)
    {
        var shown = Shown;
        if (shown.Length == 0)
        {
            Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Loaded);
            return;
        }
        var target = shown[Math.Clamp(ordinal, 0, shown.Length - 1)];
        List.SelectedItem = target;
        Dispatcher.UIThread.Post(() =>
        {
            if (List.ContainerFromItem(target) is ListBoxItem item) item.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);
    }
}
