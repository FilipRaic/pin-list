using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PinList.Sync;

namespace PinList;

/// <summary>
/// The phone app: the cards (one per board), one board full screen, and the archive. Menus, choices and the date
/// picker slide up from the bottom; changes can be undone from the bar that shows after them.
/// </summary>
public partial class PhoneView : UserControl
{
    readonly ObservableCollection<BoardCard> _cards = new();
    readonly ObservableCollection<ArchivedItem> _archiveShown = new();
    readonly PhoneDrag _homeDrag, _boardDrag;
    readonly DispatcherTimer _snackTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    AppData? _data;
    Board? _board;

    /// <summary>The current time. Tests replace it (the date picker's quick choices depend on it).</summary>
    public static Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>How long a completed item stays (crossed out) before it goes to the archive; tests shorten it.</summary>
    public static TimeSpan CompleteDelay { get; set; } = TimeSpan.FromMilliseconds(900);

    /// <summary>Asks for a file to export into (the system file picker). Tests replace it.</summary>
    public static Func<TopLevel, Task<Stream?>> PickExportFile { get; set; } = SystemExportPicker;

    /// <summary>Asks for a file to import (the system file picker). Tests replace it.</summary>
    public static Func<TopLevel, Task<Stream?>> PickImportFile { get; set; } = SystemImportPicker;

    [ExcludeFromCodeCoverage(Justification = "The phone's own file picker; tests replace it.")]
    static async Task<Stream?> SystemExportPicker(TopLevel top)
    {
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export PinList data",
            SuggestedFileName = $"PinList-{DateTime.Now:yyyy-MM-dd}.json",
            DefaultExtension = "json",
        });
        return file == null ? null : await file.OpenWriteAsync();
    }

    [ExcludeFromCodeCoverage(Justification = "The phone's own file picker; tests replace it.")]
    static async Task<Stream?> SystemImportPicker(TopLevel top)
    {
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import PinList data" });
        return files.Count == 0 ? null : await files[0].OpenReadAsync();
    }

    public PhoneView()
    {
        InitializeComponent();
        Look.GrayscaleText(this);
        Cards.ItemsSource = _cards;
        ArchiveList.ItemsSource = _archiveShown;

        _homeDrag = new PhoneDrag(HomeScreen, HomeScroller, o => o is Board)
        {
            MoveOver = MoveBoardOver,
            Dropped = DropBoard,
            LongPressed = (o, _) => ShowBoardMenu((Board)o, fromCard: true),
        };
        _boardDrag = new PhoneDrag(BoardScreen, BoardScroller, o => o is TodoItem or Category)
        {
            MoveOver = MoveOnBoard,
            Dropped = DropOnBoard,
            LongPressed = (o, _) => { if (o is TodoItem i) ShowItemMenu(i); else ShowCategoryMenu((Category)o); },
        };

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
        ArchiveSearch.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) RefreshArchive(); };
        _snackTimer.Tick += (_, _) => HideSnackbar();
        App_ThemeOrDataChanged();
    }

    // ---- For tests ----
    internal PhoneDrag HomeDrag => _homeDrag;
    internal Board? OpenBoard => _board;
    internal ObservableCollection<BoardCard> CardList => _cards;
    internal ObservableCollection<ArchivedItem> ArchiveShown => _archiveShown;
    internal bool SheetOpen => SheetLayer.IsVisible;
    internal string SheetTitleText => SheetTitle.Text!;
    internal Panel? SheetPanel => SheetContent.Content as Panel;
    internal bool SnackbarShown => Snackbar.IsVisible;
    internal string SnackbarMessage => SnackbarText.Text!;

    /// <summary>Shows the data that's in the store now (at start and after an import replaced everything).</summary>
    public void Reload()
    {
        if (_data != null)
        {
            _data.Boards.CollectionChanged -= OnBoardsChanged;
            _data.Archive.CollectionChanged -= OnArchiveChanged;
        }
        _data = Store.Data;
        _data.Boards.CollectionChanged += OnBoardsChanged;
        _data.Archive.CollectionChanged += OnArchiveChanged;
        foreach (var c in _cards) c.Dispose();
        _cards.Clear();
        foreach (var b in _data.Boards) _cards.Add(new BoardCard(b));
        RefreshSync();
        RefreshArchive();
        if (_board != null && !_data.Boards.Contains(_board)) ShowHome();
    }

    void OnBoardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            _cards.Move(e.OldStartingIndex, e.NewStartingIndex);
            return;
        }
        foreach (var gone in _cards.Where(c => !_data!.Boards.Contains(c.Board)).ToList())
        {
            gone.Dispose();
            _cards.Remove(gone);
        }
        for (int i = 0; i < _data!.Boards.Count; i++)
        {
            var b = _data.Boards[i];
            int at = _cards.ToList().FindIndex(c => c.Board == b);
            if (at < 0) _cards.Insert(i, new BoardCard(b));
            else if (at != i) _cards.Move(at, i);
        }
        RefreshSync();
        if (_board != null && !_data.Boards.Contains(_board)) ShowHome(); // the open list is gone (e.g. undone)
    }

    void OnArchiveChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshArchive();

    /// <summary>The app theme changed (or the app started): colors of the cards screen, menus and the archive.</summary>
    public void App_ThemeOrDataChanged()
    {
        if (_board != null) ApplyBoardLook(_board);
    }

    // ================= Screens =================

    // ================= Google sync =================

    SyncService? _sync;

    /// <summary>The phone's sync (null when this build or phone has none: then nothing about sync shows).</summary>
    public SyncService? Sync
    {
        get => _sync;
        set
        {
            if (_sync != null)
            {
                _sync.Changed -= RefreshSync;
                _sync.Message -= ShowMessage;
            }
            _sync = value;
            if (_sync != null)
            {
                _sync.IsBusy = o => IsDragging || SyncService.IsEditing(o); // spec 4.6
                _sync.Changed += RefreshSync;
                _sync.Message += ShowMessage;
            }
            RefreshSync();
        }
    }

    /// <summary>A card, category or item is held or dragged right now.</summary>
    internal bool IsDragging => _homeDrag.Lifted != null || _boardDrag.Lifted != null;

    /// <summary>The sync icons on the cards and in the open list's header.</summary>
    void RefreshSync()
    {
        foreach (var c in _cards) c.RefreshSync(_sync?.StateOf(c.Board));
        if (_board == null) return;
        var look = _sync == null ? null : SyncIcon.For(_sync.StateOf(_board), DateTime.UtcNow);
        BoardSyncBtn.IsVisible = look != null;
        if (look is not { } l) return;
        ((PathIcon)BoardSyncBtn.Content!).Data = (Avalonia.Media.Geometry)this.FindResource(l.IconKey)!;
        Avalonia.Automation.AutomationProperties.SetName(BoardSyncBtn, l.Tip);
        BoardSyncBtn.Classes.Set("danger", l.Danger);
    }

    /// <summary>A red icon explains what's wrong and offers the fix; other states just show.</summary>
    void BoardSync_Click(object? sender, RoutedEventArgs e)
    {
        var state = _sync!.StateOf(_board!);
        if (state.Status != SyncStatus.Problem) return;
        if (_sync.NeedsSignIn)
            ShowSheet("Google signed you out. Sign in again to keep syncing.",
                ("Sign in again", () => _ = _sync.SignIn(System.Threading.CancellationToken.None), false),
                ("Not now", () => { }, false));
        else
            ShowSheet(state.Problem == "Can't reach Google Drive."
                    ? "Can't reach Google Drive. PinList keeps trying, and your changes stay on this phone until it works again."
                    : state.Problem!,
                ("OK", () => { }, false));
    }

    /// <summary>The ⋮ menu's "Google sync" (the same choices as the desktop's tray menu).</summary>
    void ShowSyncSheet()
    {
        var sync = _sync!;
        if (!sync.IsAvailable) ShowSheet(sync.UnavailableText, ("OK", () => { }, false));
        else if (sync.Account is not { } account)
            ShowSheet("Google sync", ("Sign in with Google", () => _ = sync.SignIn(System.Threading.CancellationToken.None), false));
        else
            ShowSheet($"Signed in as {account.Email} · Last synced: {SyncIcon.Ago(sync.LastSyncUtc, DateTime.UtcNow)}",
                ("Sync now", () => _ = sync.SyncNow(), false),
                ("Sign out", () => _ = sync.SignOut(false), false),
                ("Sign out and delete cloud data", () => ShowSheet(
                    "This removes PinList's data from Google Drive for all your devices. Your lists stay on every device.",
                    ("Delete", () => _ = sync.SignOut(true), true),
                    ("Cancel", () => { }, false)), true));
    }

    void ShowHome()
    {
        _board = null;
        BoardScope.IsVisible = false;
        ArchiveScreen.IsVisible = false;
        HomeScreen.IsVisible = true;
        AddBoardButton.IsVisible = true;
    }

    internal void ShowBoard(Board board)
    {
        _board = board;
        ApplyBoardLook(board);
        Categories.ItemsSource = board.Categories;
        UpdateBoardTitle();
        RefreshSync();
        BoardScroller.Offset = default;
        HomeScreen.IsVisible = false;
        AddBoardButton.IsVisible = false;
        ArchiveScreen.IsVisible = false;
        BoardScope.IsVisible = true;
    }

    void ApplyBoardLook(Board board)
    {
        var style = Themes.Get(board.Style);
        Themes.ApplyBoard(BoardScreen.Resources, style, transparent: false);
        BoardScope.RequestedThemeVariant = style.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    void UpdateBoardTitle()
    {
        if (_board == null) return;
        bool has = !string.IsNullOrWhiteSpace(_board.Title);
        BoardTitle.Text = has ? _board.Title : "No title";
        BoardTitle.Opacity = has ? 1 : 0.6;
    }

    internal void ShowArchive()
    {
        ArchiveSearch.Text = "";
        RefreshArchive();
        HomeScreen.IsVisible = false;
        AddBoardButton.IsVisible = false;
        BoardScope.IsVisible = false;
        ArchiveScreen.IsVisible = true;
    }

    /// <summary>The back button or gesture: closes a menu, then goes back to the cards. False on the cards screen.</summary>
    public bool GoBack()
    {
        if (SheetLayer.IsVisible) { HideSheet(); return true; }
        if (BoardScope.IsVisible || ArchiveScreen.IsVisible)
        {
            CommitEdits();
            ShowHome();
            return true;
        }
        return false;
    }

    void Back_Click(object? sender, RoutedEventArgs e) => GoBack();

    // The phone's back button or gesture. Not handled on the cards screen, so Android closes the app.
    TopLevel? _backSource;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _backSource = TopLevel.GetTopLevel(this)!;
        _backSource.AddHandler(TopLevel.BackRequestedEvent, OnBackRequested);
        _inputPane = _backSource.InputPane;
        if (_inputPane != null) _inputPane.StateChanged += OnInputPaneChanged; // none in tests
    }

    // The on-screen keyboard: the screen ends above it, so what's typed (and Save) stays visible.
    Avalonia.Controls.Platform.IInputPane? _inputPane;

    [ExcludeFromCodeCoverage(Justification = "Only the phone has an on-screen keyboard; tests call KeyboardChanged.")]
    void OnInputPaneChanged(object? sender, Avalonia.Controls.Platform.InputPaneStateEventArgs e) =>
        KeyboardChanged(e.NewState == Avalonia.Controls.Platform.InputPaneState.Open ? e.EndRect.Height : 0);

    /// <summary>The keyboard now covers this much of the bottom of the screen (0: closed).</summary>
    internal void KeyboardChanged(double height) => Root.Margin = new Thickness(0, 0, 0, Math.Max(0, height));

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _backSource!.RemoveHandler(TopLevel.BackRequestedEvent, OnBackRequested);
        if (_inputPane != null) _inputPane.StateChanged -= OnInputPaneChanged;
        _inputPane = null;
        _backSource = null;
    }

    void OnBackRequested(object? sender, RoutedEventArgs e) => e.Handled = GoBack();

    void Archive_Click(object? sender, RoutedEventArgs e) => ShowArchive();

    void Card_Tapped(object? sender, TappedEventArgs e)
    {
        if (_homeDrag.SuppressTap) return;
        ShowBoard((Board)((Control)sender!).Tag!);
    }

    void NewBoard_Click(object? sender, RoutedEventArgs e)
    {
        var b = Actions.NewBoard(new WorkArea(0, 0, 1280, 800), _cards.LastOrDefault()?.Board);
        b.Title = "";
        b.Style = "Light";
        ShowBoard(b);
        BeginTitleEdit();
    }

    // ================= Board title =================

    void BoardTitle_Tapped(object? sender, TappedEventArgs e) => BeginTitleEdit();

    void BeginTitleEdit()
    {
        BoardTitleBox.Text = _board!.Title;
        BoardTitle.IsVisible = false;
        BoardTitleBox.IsVisible = true;
        Dispatcher.UIThread.Post(() => { BoardTitleBox.Focus(); BoardTitleBox.CaretIndex = BoardTitleBox.Text?.Length ?? 0; }, DispatcherPriority.Loaded);
    }

    void CommitTitle()
    {
        if (!BoardTitleBox.IsVisible || _board == null) return;        BoardTitleBox.IsVisible = false;
        BoardTitle.IsVisible = true;
        Actions.RenameBoard(_board, (BoardTitleBox.Text ?? "").Trim());
        UpdateBoardTitle();
    }

    // ================= Categories and items =================

    void AddCategory_Click(object? sender, RoutedEventArgs e)
    {
        var cat = Actions.AddCategory(_board!);
        BeginEdit(cat);
        Dispatcher.UIThread.Post(() => BoardScroller.ScrollToEnd(), DispatcherPriority.Loaded);
    }

    void CategoryHeader_Tapped(object? sender, TappedEventArgs e)
    {
        var cat = (Category)((Control)sender!).Tag!;
        if (_boardDrag.SuppressTap || cat.IsEditing) return;
        cat.Collapsed = !cat.Collapsed;
        Store.MarkDirty();
        A11y.Announce(this, cat.Collapsed ? $"{cat.Name} collapsed" : $"{cat.Name} expanded");
    }

    void ItemText_Tapped(object? sender, TappedEventArgs e)
    {
        if (_boardDrag.SuppressTap) return;
        BeginEdit((TodoItem)((Control)sender!).DataContext!);
    }

    /// <summary>Opens the text box of an item or category name and puts the keyboard in it.</summary>
    void BeginEdit(Observable target)
    {
        CommitEdits();
        if (target is TodoItem i) i.IsEditing = true;
        else ((Category)target).IsEditing = true;
        Dispatcher.UIThread.Post(() =>
        {
            var box = Root.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(t => t.Classes.Contains("phoneedit") && ReferenceEquals(t.DataContext, target));
            if (box == null) return;
            box.Text = target is TodoItem it ? it.Text : ((Category)target).Name;
            box.Focus();
            if (target is Category { Name: "New category" }) box.SelectAll();
            else box.CaretIndex = box.Text.Length;
        }, DispatcherPriority.Loaded);
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox tb) return;
        if (tb == BoardTitleBox && e.Key == Key.Enter)
        {
            CommitTitle();
            e.Handled = true;
        }
        else if (tb.Classes.Contains("phoneadd") && e.Key == Key.Enter)
        {
            var text = (tb.Text ?? "").Trim();
            if (text.Length > 0)
            {
                Actions.AddItem((Category)tb.Tag!, text);
                A11y.Announce(this, $"Added {text}");
            }
            tb.Text = ""; // the keyboard stays open for the next item
            e.Handled = true;
        }
        else if (tb.Classes.Contains("phoneedit") && e.Key == Key.Enter)
        {
            Commit(tb);
            e.Handled = true;
        }
        else if (tb.Classes.Contains("phoneedit") && e.Key == Key.Escape)
        {
            if (tb.DataContext is TodoItem i) i.IsEditing = false;
            else ((Category)tb.DataContext!).IsEditing = false;
            e.Handled = true;
        }
    }

    void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox tb && tb.Classes.Contains("phoneedit")) Commit(tb);
        else if (e.Source == BoardTitleBox) CommitTitle();
    }

    static void Commit(TextBox tb)
    {
        var text = (tb.Text ?? "").Replace("\r\n", "\n").Trim();
        switch (tb.DataContext)
        {
            case TodoItem i when i.IsEditing:
                i.IsEditing = false;
                if (text.Length == 0) Actions.Delete(i);
                else Actions.SetItemText(i, text);
                break;
            case Category c when c.IsEditing:
                c.IsEditing = false;
                if (text.Length > 0) Actions.RenameCategory(c, text);
                break;
        }
    }

    /// <summary>The app goes to the background: keep what's being typed (the system may end the app next).</summary>
    public void SaveTyping() => CommitEdits();

    /// <summary>Saves whatever is being typed (before leaving the board).</summary>
    void CommitEdits()
    {
        foreach (var tb in Root.GetVisualDescendants().OfType<TextBox>().Where(t => t.Classes.Contains("phoneedit") && t.IsEffectivelyVisible).ToList())
            Commit(tb);
        CommitTitle();
    }

    void Check_Click(object? sender, RoutedEventArgs e)
    {
        ToggleCompleting((TodoItem)((Control)sender!).Tag!);
    }

    /// <summary>Starts completing (with a moment to change one's mind), or cancels if it's already completing.</summary>
    internal void ToggleCompleting(TodoItem item)
    {
        if (item.IsCompleting)
        {
            item.IsCompleting = false;
            A11y.Announce(this, "Cancelled");
            return;
        }
        item.IsCompleting = true;
        var timer = new DispatcherTimer { Interval = CompleteDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!item.IsCompleting || Store.Locate(item) == null) return;
            Actions.Complete(item);
            ShowUndo($"Completed {FirstLine(item.Text)}");
        };
        timer.Start();
    }

    static string FirstLine(string s)
    {
        int i = s.IndexOf('\n');
        return i < 0 ? s : s[..i];
    }

    // ================= Drag and drop (one undo step per drag) =================

    int? _boardFrom;
    (Category cat, int index)? _itemFrom;
    int? _categoryFrom;

    bool MoveBoardOver(object dragged, object target, Point p, Size size)
    {
        var boards = Store.Data.Boards; // only boards can be dragged on the cards screen
        int from = boards.IndexOf((Board)dragged), to = boards.IndexOf((Board)target);
        _boardFrom ??= from;
        boards.Move(from, to);
        return true;
    }

    void DropBoard(object dragged)
    {
        if (_boardFrom is not { } origin) return; // held and moved, but never over another card
        _boardFrom = null;
        var board = (Board)dragged;
        var boards = Store.Data.Boards;
        int final = boards.IndexOf(board);
        // put the order back as it was and make the whole drag one undoable move
        boards.Move(final, origin);
        Actions.MoveBoard(board, final);
    }

    bool MoveOnBoard(object dragged, object target, Point p, Size size)
    {
        bool lower = p.Y > size.Height / 2;
        switch (dragged, target)
        {
            case (TodoItem d, TodoItem t) when Store.Locate(t) is { } loc && Store.Locate(d) is { } from:
                _itemFrom ??= (from.category, from.category.Items.IndexOf(d));
                RawMove(d, from.category, loc.category, loc.category.Items.IndexOf(t) + (lower ? 1 : 0));
                return true;
            case (TodoItem d, Category c) when Store.Locate(d) is { } from:
                _itemFrom ??= (from.category, from.category.Items.IndexOf(d));
                RawMove(d, from.category, c, 0);
                return true;
            case (Category d, Category t):
                var cats = _board!.Categories;
                int fromIndex = cats.IndexOf(d);
                _categoryFrom ??= fromIndex;
                cats.Move(fromIndex, cats.IndexOf(t));
                return true;
            default:
                return false; // e.g. a category over an item: keep looking (to the item's category)
        }
    }

    /// <summary>Moves an item right away while dragging (the undoable move is made when the finger lifts).</summary>
    static void RawMove(TodoItem item, Category from, Category to, int index)
    {
        int old = from.Items.IndexOf(item);
        if (from == to)
        {
            if (old < index) index--;
            index = Math.Clamp(index, 0, to.Items.Count - 1);
            if (old != index) to.Items.Move(old, index);
            return;
        }
        from.Items.RemoveAt(old);
        to.Items.Insert(Math.Clamp(index, 0, to.Items.Count), item);
    }

    void DropOnBoard(object dragged)
    {
        if (dragged is TodoItem item && _itemFrom is { } origin && Store.Locate(item) is { } now)
        {
            _itemFrom = null;
            var finalCat = now.category;
            int finalIndex = finalCat.Items.IndexOf(item);
            // put it back where it was and make the whole drag one undoable move
            finalCat.Items.RemoveAt(finalIndex);
            origin.cat.Items.Insert(Math.Min(origin.index, origin.cat.Items.Count), item);
            Actions.MoveItem(item, finalCat, finalCat == origin.cat && finalIndex > origin.index ? finalIndex + 1 : finalIndex);
        }
        else if (dragged is Category cat && _categoryFrom is { } from)
        {
            _categoryFrom = null;
            int final = _board!.Categories.IndexOf(cat);
            _board.Categories.Move(final, from);
            Actions.MoveCategory(cat, _board, final > from ? final + 1 : final);
        }
        _itemFrom = null;
        _categoryFrom = null;
    }

    // ================= Menus (bottom sheet) =================

    /// <summary>Shows a list of choices sliding up from the bottom; a tap outside or Back closes it.</summary>
    internal void ShowSheet(string title, params (string text, Action action, bool danger)[] options)
    {
        var panel = new StackPanel();
        foreach (var (text, action, danger) in options)
        {
            var b = new Button { Content = text };
            b.Classes.Add("option");
            if (danger) b.Classes.Add("danger");
            b.Click += (_, _) => { HideSheet(); action(); };
            panel.Children.Add(b);
        }
        ShowSheetContent(title, panel);
    }

    void ShowSheetContent(string title, Control content)
    {
        SheetTitle.Text = title;
        SheetContent.Content = content;
        SheetLayer.IsVisible = true;
    }

    internal void HideSheet()
    {
        SheetLayer.IsVisible = false;
        SheetContent.Content = null;
    }

    void SheetBackground_Tapped(object? sender, TappedEventArgs e) => HideSheet();

    internal void ShowItemMenu(TodoItem item)
    {        var options = new System.Collections.Generic.List<(string, Action, bool)>
        {
            ("Edit", () => BeginEdit(item), false),
            (item.HasDue ? "Change due date" : "Add due date", () => PickDate(item, reminder: false), false),
        };
        if (item.HasDue) options.Add(("Remove due date", () => Actions.SetDueDate(item, null), false));
        options.Add((item.HasPendingReminder ? "Change reminder" : "Add reminder", () => PickDate(item, reminder: true), false));
        if (item.ReminderAt != null) options.Add(("Remove reminder", () => Actions.SetReminder(item, null), false));
        if (Store.Data.Boards.Any(b => b != _board && b.Categories.Count > 0))
            options.Add(("Move to list", () => ShowMoveToBoard(item), false));
        options.Add(("Delete", () => { Actions.Delete(item); ShowUndo($"Deleted {FirstLine(item.Text)}"); }, true));
        ShowSheet(FirstLine(item.Text), options.ToArray());
    }

    void ShowMoveToBoard(TodoItem item)
    {
        var targets = Store.Data.Boards.Where(b => b != _board && b.Categories.Count > 0).ToList();
        ShowSheet("Move to list", targets.Select(b =>
        {
            string name = string.IsNullOrWhiteSpace(b.Title) ? "No title" : b.Title;
            return (name, (Action)(() =>
            {
                var cat = b.Categories[0];
                Actions.MoveItem(item, cat, cat.Items.Count);
                ShowUndo($"Moved to {name}");
            }), false);
        }).ToArray());
    }

    internal void ShowCategoryMenu(Category cat)
    {
        ShowSheet(cat.Name,
            ("Rename", () => BeginEdit(cat), false),
            ("Color", () => ShowSheet("Color", Actions.Palette.Select((hex, i) =>
                (Actions.PaletteNames[i], (Action)(() => Actions.SetCategoryColor(cat, hex)), false)).ToArray()), false),
            ("Delete category", () => { Actions.DeleteCategory(cat); ShowUndo($"Deleted category {cat.Name}"); }, true));
    }

    void BoardMenu_Click(object? sender, RoutedEventArgs e)
    {
        ShowBoardMenu(_board!, fromCard: false);
    }

    internal void ShowBoardMenu(Board board, bool fromCard)
    {
        string name = string.IsNullOrWhiteSpace(board.Title) ? "No title" : board.Title;
        var options = new System.Collections.Generic.List<(string, Action, bool)>();
        if (fromCard) options.Add(("Open", () => ShowBoard(board), false));
        else options.Add(("Rename", BeginTitleEdit, false));
        options.Add(("Color", () => ShowSheet("Color", Themes.BoardStyles.Select(s =>
            (s.Name, (Action)(() => SetBoardStyle(board, s.Key)), false)).ToArray()), false));
        if (_sync?.IsAvailable == true)
            options.Add(((board.Synced ? "✓ " : "") + "Sync this list", () => _ = _sync.SetSynced(board, !board.Synced), false));
        options.Add(("Delete list", () => ConfirmDeleteBoard(board, name), true));
        ShowSheet(name, options.ToArray());
    }

    void SetBoardStyle(Board board, string key)
    {
        board.Style = key;
        Store.MarkDirty();
        _cards.First(c => c.Board == board).RefreshColors();
        if (_board == board) ApplyBoardLook(board);
    }

    void ConfirmDeleteBoard(Board board, string name)
    {
        int items = board.Categories.Sum(c => c.Items.Count);
        var question = items > 0 ? $"Delete \"{name}\" with {items} items? They don't go to the archive." : $"Delete \"{name}\"?";
        if (board.Synced) question += " It will also be deleted from your other devices.";
        ShowSheet(question,
            ("Delete", () =>
            {
                Actions.DeleteBoard(board); // asked first, so not undoable
                if (_board == board) ShowHome();
            }, true),
            ("Cancel", () => { }, false));
    }

    void AppMenu_Click(object? sender, RoutedEventArgs e)
    {
        var options = new System.Collections.Generic.List<(string, Action, bool)>
        {
            ("Archive", ShowArchive, false),
            ("App theme", () => ShowSheet("App theme", Themes.AppThemes.Select(t =>
                ((Store.Data.Settings.Theme == t.Key ? "✓ " : "") + t.Name, (Action)(() => SetTheme(t.Key)), false)).ToArray()), false),
        };
        if (_sync != null) options.Add(("Google sync", ShowSyncSheet, false));
        options.Add(("Export", () => _ = Export(), false));
        options.Add(("Import", () => _ = Import(), false));
        ShowSheet("PinList", options.ToArray());
    }

    /// <summary>Changes the app theme (the head app applies it to the app's colors).</summary>
    public static Action<string> SetTheme { get; set; } = _ => { };

    // ================= Date and reminder picker =================

    void PickDate(TodoItem item, bool reminder)
    {
        var now = Clock();
        var today = now.Date;
        var initial = reminder ? (item.HasPendingReminder ? item.ReminderAt : item.DueDate) : item.DueDate;
        var day = initial?.Date is { } d && d >= today ? d : today;

        var panel = new StackPanel { Margin = new Thickness(16, 0, 16, 0), Spacing = 8 };
        var chips = new WrapPanel();
        var cal = new Calendar { SelectedDate = day, DisplayDate = day, FirstDayOfWeek = DayOfWeek.Monday, HorizontalAlignment = HorizontalAlignment.Center };
        cal.BlackoutDates.Add(new CalendarDateRange(DateTime.MinValue, today.AddDays(-1)));
        var time = new TextBox { Text = initial?.ToString("HH:mm") ?? "", PlaceholderText = "Time, e.g. 9:30", IsVisible = reminder };
        Avalonia.Automation.AutomationProperties.SetName(time, "Time");
        var error = new TextBlock { IsVisible = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable("DangerFill"));

        void Done(DateTime value)
        {
            HideSheet();
            if (reminder) Actions.SetReminder(item, value);
            else Actions.SetDueDate(item, value);
            A11y.Announce(this, reminder ? $"Reminder set for {item.ReminderText}" : $"Due {item.DueText}");
        }

        void Chip(string text, Func<DateTime> value)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) };
            b.Classes.Add("dialog");
            b.Click += (_, _) => Done(value());
            chips.Children.Add(b);
        }

        if (reminder)
        {
            Chip("In 1 hour", () => { var t = Clock().AddHours(1); return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0); });
            if (now.Hour < 17) Chip("This evening", () => today.AddHours(18));
            Chip("Tomorrow 9:00", () => today.AddDays(1).AddHours(9));
        }
        else
        {
            Chip("Today", () => today);
            Chip("Tomorrow", () => today.AddDays(1));
            int days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
            Chip("Next week", () => today.AddDays(days == 0 ? 7 : days));
        }

        var save = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Right };
        save.Classes.Add("dialog");
        save.Classes.Add("accent");
        save.Click += (_, _) => Save();
        time.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Save(); // the keyboard's ✓; Save itself may be under the keyboard
        };

        void Save()
        {
            string? problem = null;
            DateTime value = default;
            if (cal.SelectedDate is not { } picked) problem = "Pick a day.";
            else if (!reminder) value = picked.Date;
            else if (!DateText.TryParseTime(time.Text, out var t)) problem = "Enter a time like 9:30 or 17:00.";
            else if (picked.Date + t <= Clock()) problem = "That time has already passed.";
            else value = picked.Date + t;

            if (problem != null)
            {
                error.Text = problem;
                error.IsVisible = true;
                A11y.Announce(this, problem);
                return;
            }
            Done(value);
        }

        panel.Children.Add(chips);
        panel.Children.Add(cal);
        panel.Children.Add(time);
        panel.Children.Add(error);
        panel.Children.Add(save);
        ShowSheetContent(reminder ? (item.HasPendingReminder ? "Change reminder" : "Add reminder")
                                  : (item.HasDue ? "Change due date" : "Add due date"), panel);
    }

    // ================= Archive =================

    void RefreshArchive()
    {
        var q = (ArchiveSearch.Text ?? "").Trim();
        _archiveShown.Clear();
        foreach (var a in Store.Data.Archive.Where(a => q.Length == 0
                     || a.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                     || a.CategoryName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                     || a.BoardTitle.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
            _archiveShown.Add(a);
        ArchiveEmpty.IsVisible = Store.Data.Archive.Count == 0;
    }

    void ArchiveItem_Tapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is ArchivedItem a) ShowArchivedMenu(a);
    }

    internal void ShowArchivedMenu(ArchivedItem a) =>
        ShowSheet(FirstLine(a.Text),
            ("Restore", () =>
            {
                var done = Actions.Restore(new[] { a });
                if (done.Count == 0) ShowMessage("There is no category to restore into. Add a category to a list first.");
                else if (done[0].category.Id != a.CategoryId)
                    ShowUndo($"\"{a.CategoryName}\" no longer exists, so it went to \"{done[0].category.Name}\"");
                else ShowUndo("Restored");
            }, false),
            ("Delete permanently", () => { Actions.DeleteArchived(new[] { a }); ShowUndo("Deleted permanently"); }, true));

    // ================= Undo bar =================

    /// <summary>Says what just happened, with an Undo button, for a few seconds.</summary>
    internal void ShowUndo(string message) => ShowSnackbar(message, undo: true);

    void ShowMessage(string message) => ShowSnackbar(message, undo: false);

    /// <summary>Messages from PinList.Core (e.g. "the data file was damaged, the backup was loaded").</summary>
    public void ShowMessageForStore(string message) => ShowMessage(message);

    void ShowSnackbar(string message, bool undo)
    {
        SnackbarText.Text = message;
        UndoButton.IsVisible = undo;
        Snackbar.IsVisible = true;
        _snackTimer.Stop();
        _snackTimer.Start();
        A11y.Announce(this, undo ? message + ". Undo is available." : message);
    }

    void HideSnackbar()
    {
        _snackTimer.Stop();
        Snackbar.IsVisible = false;
    }

    void Undo_Click(object? sender, RoutedEventArgs e)
    {
        HideSnackbar();
        A11y.Announce(this, Actions.Undo(out var what) ? "Undone: " + what : "Nothing to undo");
        UpdateBoardTitle();
    }

    // ================= Export and import =================

    internal async Task Export()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            await using var stream = await PickExportFile(top);
            if (stream == null) return;
            var bytes = Encoding.UTF8.GetBytes(Store.ExportJson());
            await stream.WriteAsync(bytes);
            ShowMessage("Exported");
        }
        catch (Exception ex)
        {
            Store.Log($"Export: {ex}");
            ShowMessage("Export failed: " + ex.Message);
        }
    }

    internal async Task Import()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        AppData imported;
        try
        {
            await using var stream = await PickImportFile(top);
            if (stream == null) return;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            imported = Store.Parse(await reader.ReadToEndAsync(), out _);
        }
        catch (Exception ex)
        {
            Store.Log($"Import: {ex}");
            ShowMessage("This file can't be imported. It isn't a PinList export, or it is damaged.");
            return;
        }

        int lists = imported.Boards.Count, items = imported.Boards.SelectMany(b => b.Categories).Sum(c => c.Items.Count);
        ShowSheet($"Import {lists} list{(lists == 1 ? "" : "s")} with {items} item{(items == 1 ? "" : "s")}?" +
                  (Store.HasSyncedBoards ? " Synced lists aren't replaced." : ""),
            ("Add next to mine", () => { Store.Merge(imported); ShowMessage("Imported"); }, false),
            ("Replace everything (a copy of mine is kept)", () =>
            {
                try
                {
                    Store.ReplaceAll(imported);
                    Actions.ClearUndo();
                    Reload();
                    ShowHome();
                    ShowMessage("Imported");
                }
                catch (Exception ex)
                {
                    Store.Log($"Import: {ex}");
                    ShowMessage("Import failed: " + ex.Message);
                }
            }, true),
            ("Cancel", () => { }, false));
    }
}
