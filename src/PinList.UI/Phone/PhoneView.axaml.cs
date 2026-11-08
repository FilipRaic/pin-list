using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
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

/// <summary>The three screens of the bottom bar.</summary>
public enum PhoneSection { Lists, Archive, Settings }

/// <summary>One choice of a bottom sheet. <paramref name="IsOn"/> (not null) draws a switch on its right.</summary>
internal sealed record SheetOption(string Text, Action Run, bool Danger = false, string? Icon = null, bool? IsOn = null);

/// <summary>
/// The phone app: the cards (one per board), one board full screen, the archive and the settings, with a bottom bar
/// for the three tabs. Menus, choices and the date picker slide up from the bottom; changes can be undone from the
/// bar that shows after them.
/// </summary>
public partial class PhoneView : UserControl
{
    readonly ObservableCollection<BoardCard> _cards = new();
    readonly ObservableCollection<object> _archiveRows = new();
    readonly PhoneDrag _homeDrag, _boardDrag;
    readonly DispatcherTimer _snackTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    AppData? _data;
    Board? _board;

    /// <summary>The current time. Tests replace it (the date picker's quick choices depend on it).</summary>
    public static Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>Asks for a file to export into (the system file picker). Tests replace it.</summary>
    public static Func<TopLevel, Task<Stream?>> PickExportFile { get; set; } = SystemExportPicker;

    /// <summary>Opens a web page or a download in the phone's browser. Tests replace it.</summary>
    public static Func<TopLevel, string, Task> OpenUrl { get; set; } = SystemOpenUrl;

    [ExcludeFromCodeCoverage(Justification = "Opens the phone's browser; tests replace it.")]
    static async Task SystemOpenUrl(TopLevel top, string url) => await top.Launcher.LaunchUriAsync(new Uri(url));

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
        ArchiveList.ItemTemplate = ArchiveRowTemplate.Instance;
        ArchiveList.ItemsSource = _archiveRows;
        ArchiveList.ContainerPrepared += (_, e) =>
        {
            bool header = e.Container.DataContext is ArchiveDayHeader;
            e.Container.Focusable = !header;
            e.Container.IsHitTestVisible = !header; // day headings are not rows to tap
        };
        SetupSheet();

        _homeDrag = new PhoneDrag(HomeScreen, HomeScroller, o => o is Board)
        {
            LiftScale = 1.03,
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
        _snackbarAction = UndoLastChange; // the bar's button starts as Undo
        HomeEmpty.Text = EmptyText;
        // The phone's first screen with no lists: screen readers hear how to begin (the desktop's PinList window
        // does this when it shows All lists). Posted, because the view's own parts attach after it.
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!IsDesktop && Section == PhoneSection.Lists && _board == null && Store.Data.Boards.Count == 0) FocusNewList();
        });
        App_ThemeOrDataChanged();
        UpdateBar();
    }

    /// <summary>What the cards screen says when there are no lists at all.</summary>
    public const string EmptyText = "No lists yet, create one to get started";

    /// <summary>
    /// The "All lists" section of the desktop's PinList window: right click opens the menus that a long press opens on
    /// the phone, cards get an "On desktop" switch, and there is no bottom bar (the window has its own sidebar).
    /// </summary>
    public PhoneView(bool desktop) : this()
    {
        IsDesktop = desktop;
        _homeDrag.RightClickOpensMenu = _boardDrag.RightClickOpensMenu = desktop;
        _homeDrag.DragWithMouse = _boardDrag.DragWithMouse = desktop; // reorder cards, categories and items with the mouse
        HomeTitle.Text = desktop ? "All lists" : "Lists";
        NewListCard.IsVisible = desktop;
        HomeBottomSpace.IsVisible = !desktop;
        AddBoardButton.IsVisible = !desktop;
        UpdateBar();
        Reload();
    }

    public bool IsDesktop { get; }

    BoardCard NewCard(Board b) => new(b) { ShowWidgetSwitch = IsDesktop };

    // ---- For tests ----
    internal PhoneDrag HomeDrag => _homeDrag;
    internal Board? OpenBoard => _board;
    internal ObservableCollection<BoardCard> CardList => _cards;
    internal System.Collections.Generic.IEnumerable<ArchivedItem> ArchiveShown => _archiveRows.OfType<ArchivedItem>();
    internal System.Collections.Generic.IReadOnlyList<object> ArchiveRows => _archiveRows;
    internal PhoneSection Section { get; private set; } = PhoneSection.Lists;
    internal SettingsView? SettingsPage => _settings;
    internal bool BottomBarVisible => BottomBar.IsVisible;
    internal bool SheetOpen => SheetLayer.IsVisible;
    internal string SheetTitleText => SheetTitle.Text!;
    internal Panel? SheetPanel => SheetContent.Content as Panel;
    internal Border SheetHandleArea => SheetHandle;
    internal bool SnackbarShown => Snackbar.IsVisible;
    internal bool AddBoardButtonVisible => AddBoardButton.IsVisible;
    internal string SnackbarMessage => SnackbarText.Text!;
    internal bool HomeEmptyVisible => HomeEmpty.IsVisible;

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
        foreach (var b in _data.Boards) _cards.Add(NewCard(b));
        RefreshEmpty();
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
            if (at < 0) _cards.Insert(i, NewCard(b));
            else if (at != i) _cards.Move(at, i);
        }
        RefreshEmpty();
        // the last list went while the cards were showing: the keyboard and screen readers go to New list
        if (_data.Boards.Count == 0 && e.Action == NotifyCollectionChangedAction.Remove && _board == null
            && HomeScreen.IsEffectivelyVisible) FocusNewList();
        RefreshSync();
        if (_board != null && !_data.Boards.Contains(_board)) ShowHome(); // the open list is gone (e.g. undone)
    }

    void OnArchiveChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshArchive();

    /// <summary>The empty text shows only when there are no lists.</summary>
    void RefreshEmpty() => HomeEmpty.IsVisible = _data!.Boards.Count == 0;

    /// <summary>
    /// Puts the keyboard on New list (the dashed card on the desktop, the button on the phone). With no lists, screen
    /// readers also hear the empty text.
    /// </summary>
    public void FocusNewList()
    {
        (IsDesktop ? (Control)NewListCard : AddBoardButton).Focus();
        if (Store.Data.Boards.Count == 0) A11y.Announce(this, EmptyText);
    }

    /// <summary>The app theme changed (or the app started): colors of the cards screen, menus and the archive.</summary>
    public void App_ThemeOrDataChanged()
    {
        ChromeLook.Apply(this);
        if (_board != null) ApplyBoardLook(_board);
        _settingsHost?.RaiseChanged();
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
                new SheetOption("Sign in again", () => _ = _sync.SignIn(System.Threading.CancellationToken.None), Icon: "Line.Sync"),
                new SheetOption("Not now", () => { }));
        else
            ShowSheet(state.Problem == "Can't reach Google Drive."
                    ? "Can't reach Google Drive. PinList keeps trying, and your changes stay on this phone until it works again."
                    : state.Problem!,
                new SheetOption("OK", () => { }));
    }

    // ================= Updates =================

    Updates? _updates;

    /// <summary>The update check (null when this build has none: then nothing about updates shows).</summary>
    public Updates? UpdateCheck
    {
        get => _updates;
        set
        {
            if (_updates != null)
            {
                _updates.Found -= OnUpdateFound;
                _updates.Changed -= OnUpdatesChanged;
            }
            _updates = value;
            RefreshUpdateRow();
            if (_updates == null) return;
            _updates.Found += OnUpdateFound;
            _updates.Changed += OnUpdatesChanged;
            if (_updates.Enabled == null) Dispatcher.UIThread.Post(AskAboutUpdates);
        }
    }

    /// <summary>Asked on start until it's answered (tapping outside the question asks again next time).</summary>
    internal void AskAboutUpdates()
    {
        if (_updates is not { Enabled: null } updates) return;
        var text = new TextBlock
        {
            Text = "PinList can look for a new version about once a day and tell you when there is one. " +
                   "It only asks GitHub for the number of the latest version and what's new in it. " +
                   "Nothing about you or your lists is sent. You can change this later in Settings.",
            FontSize = 14,
            LineHeight = 14 * 1.4,
            Margin = new Thickness(22, 4, 22, 10),
        };
        text.Classes.Add("sheettext");
        // "Check for updates" is too long for half the width: the buttons go under each other
        ShowButtonSheet("Check for updates?", text, stacked: true,
            SheetButton("Don't check", null, () => updates.Enabled = false),
            SheetButton("Check for updates", "Line.Download", () => updates.Enabled = true));
    }

    /// <summary>The update check or its setting changed: Settings shows the new state.</summary>
    void OnUpdatesChanged()
    {
        RefreshUpdateRow();
        _settingsHost?.RaiseChanged();
    }

    /// <summary>
    /// While a newer version is out: "Get PinList x.y" at the top of Settings, and a dot on the Settings tab.
    /// </summary>
    void RefreshUpdateRow()
    {
        var available = _updates?.Available;
        UpdateRow.IsVisible = available != null;
        UpdateDot.IsVisible = available != null;
        if (available != null) UpdateRow.Content = $"Get PinList {available.Version}";
        AutomationProperties.SetName(TabSettings, available != null ? $"Settings, PinList {available.Version} is available" : "Settings");
    }

    void UpdateRow_Click(object? sender, RoutedEventArgs e) => OpenUpdate();

    /// <summary>A new version, found for the first time: a sheet with what's new in it, to get it now or later.</summary>
    void OnUpdateFound(UpdateInfo update)
    {
        string title = $"PinList {update.Version} is available";
        var notes = ReleaseNotesView.Build(update.Notes, 14);
        var body = notes == null ? null : new ScrollViewer
        {
            Content = notes,
            MaxHeight = 320,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Padding = new Thickness(22, 4, 22, 10),
        };
        ShowButtonSheet(title, body, stacked: false,
            SheetButton("Later", null, () => { }),
            SheetButton("Get it", "Line.Download", OpenUpdate));
        A11y.Announce(this, $"{title}. Get it now, or later in Settings.");
    }

    /// <summary>
    /// A sheet with <paramref name="body"/> (text, release notes) above two buttons: <paramref name="quiet"/> and the
    /// main one, filled with the accent color like New list. Side by side with the main one on the right, or
    /// <paramref name="stacked"/> under each other with the main one at the bottom (for longer labels).
    /// </summary>
    void ShowButtonSheet(string title, Control? body, bool stacked, Button quiet, Button main)
    {
        main.Classes.Add("accent");
        Panel buttons;
        if (stacked)
            buttons = new StackPanel { Spacing = 10, Children = { quiet, main } };
        else
        {
            Grid.SetColumn(main, 2);
            buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*"), Children = { quiet, main } };
        }
        buttons.Margin = new Thickness(22, 10, 22, 0);

        var panel = new StackPanel();
        if (body != null) panel.Children.Add(body);
        panel.Children.Add(buttons);
        ShowSheetContent(title, panel);
    }

    /// <summary>A button of the row at the bottom of a sheet: closes the sheet, then runs <paramref name="run"/>.</summary>
    Button SheetButton(string text, string? icon, Action run)
    {
        object content = text;
        if (icon != null)
            content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new LineIcon { Data = (Avalonia.Media.Geometry)Application.Current!.FindResource(icon)!, Width = 20, Height = 20 },
                    new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
                },
            };
        var button = new Button { Content = content };
        button.Classes.Add("sheetbutton");
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => { HideSheet(); run(); };
        return button;
    }

    /// <summary>
    /// Downloads the new version's APK in the browser. Without an APK in the release, it opens the Download section
    /// of PinList's website.
    /// </summary>
    void OpenUpdate()
    {
        if (TopLevel.GetTopLevel(this) is { } top) _ = OpenUrl(top, _updates?.Available?.ApkUrl ?? Updates.DownloadsPage);
    }

    internal void ShowHome()
    {
        bool wasBoard = _board != null;
        if (_board != null) _board.PropertyChanged -= OnOpenBoardChanged;
        _board = null;
        if (wasBoard) SlideOutBoard();
        else BoardScope.IsVisible = false;
        ArchiveScreen.IsVisible = false;
        SettingsScreen.IsVisible = false;
        HomeScreen.IsVisible = true;
        AddBoardButton.IsVisible = !IsDesktop;
        Section = PhoneSection.Lists;
        UpdateBar();
        if (!wasBoard) FadeIn(HomeScreen);
        if (Store.Data.Boards.Count == 0) FocusNewList();
    }

    /// <summary>The bottom bar shows on the three screens, not inside an open list (and not in the PinList window).</summary>
    void UpdateBar()
    {
        BottomBar.IsVisible = !IsDesktop && _board == null;
        TabLists.Classes.Set("selected", Section == PhoneSection.Lists);
        TabArchive.Classes.Set("selected", Section == PhoneSection.Archive);
        TabSettings.Classes.Set("selected", Section == PhoneSection.Settings);
    }

    /// <summary>Switches to one of the three screens (the bottom bar and the tests use it).</summary>
    internal void ShowSection(PhoneSection section)
    {
        CommitEdits();
        switch (section)
        {
            case PhoneSection.Lists: ShowHome(); break;
            case PhoneSection.Archive: ShowArchive(); break;
            default: ShowSettings(); break;
        }
    }

    void Tab_Click(object? sender, RoutedEventArgs e) =>
        ShowSection(Enum.Parse<PhoneSection>((string)((Control)sender!).Tag!));

    internal void ShowBoard(Board board)
    {
        if (_board != null) _board.PropertyChanged -= OnOpenBoardChanged;
        _board = board;
        board.PropertyChanged += OnOpenBoardChanged;
        ApplyBoardLook(board);
        Categories.ItemsSource = board.Categories;
        UpdateBoardTitle();
        RefreshSync();
        BoardScroller.Offset = default;
        HomeScreen.IsVisible = false;
        AddBoardButton.IsVisible = false;
        ArchiveScreen.IsVisible = false;
        SettingsScreen.IsVisible = false;
        BoardScope.IsVisible = true;
        UpdateBar();
        SlideInBoard();
    }

    void ApplyBoardLook(Board board)
    {
        var style = Themes.Get(board.Style);
        Themes.ApplyBoard(BoardScreen.Resources, style, transparent: false);
        BoardScope.RequestedThemeVariant = style.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        // the category colors get a readable shade on this list color
        ChromeLook.SetStyleKey(BoardScreen, style.Key);
        ChromeLook.SetVersion(BoardScreen, ChromeLook.GetVersion(BoardScreen) + 1);
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
        bool wasBoard = _board != null;
        if (wasBoard) _board!.PropertyChanged -= OnOpenBoardChanged;
        _board = null;
        HomeScreen.IsVisible = false;
        AddBoardButton.IsVisible = false;
        BoardScope.IsVisible = false;
        SettingsScreen.IsVisible = false;
        ArchiveScreen.IsVisible = true;
        Section = PhoneSection.Archive;
        UpdateBar();
        FadeIn(ArchiveScreen);
    }

    /// <summary>The settings tab (the page is made when it is first needed).</summary>
    internal void ShowSettings()
    {
        if (_settings == null)
        {
            _settingsHost = new PhoneSettingsHost(this, ConfirmDeleteCloudData);
            _settings = new SettingsView(_settingsHost);
            _settings.Classes.Add("phone");
            _settings.Refresh();
            SettingsHost.Content = _settings;
        }
        else _settings.Refresh();
        bool wasBoard = _board != null;
        if (wasBoard) _board!.PropertyChanged -= OnOpenBoardChanged;
        _board = null;
        HomeScreen.IsVisible = false;
        AddBoardButton.IsVisible = false;
        BoardScope.IsVisible = false;
        ArchiveScreen.IsVisible = false;
        SettingsScreen.IsVisible = true;
        Section = PhoneSection.Settings;
        UpdateBar();
        FadeIn(SettingsScreen);
    }

    SettingsView? _settings;
    PhoneSettingsHost? _settingsHost;

    /// <summary>"Sign out and delete cloud data" asks first, in a sheet.</summary>
    void ConfirmDeleteCloudData(Action delete) =>
        ShowSheet("This removes PinList's data from Google Drive for all your devices. Your lists stay on every device.",
            new SheetOption("Delete", delete, Danger: true, Icon: "Line.Delete"),
            new SheetOption("Cancel", () => { }));

    /// <summary>The back button or gesture: closes a menu, then goes back to the cards. False on the cards screen.</summary>
    public bool GoBack()
    {
        if (SheetLayer.IsVisible) { HideSheet(); return true; }
        if (_board != null || Section != PhoneSection.Lists)
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

    void Card_Tapped(object? sender, TappedEventArgs e)
    {
        if (_homeDrag.SuppressTap) return;
        if (e.Source is Visual v && v.FindAncestorOfType<ToggleSwitch>(includeSelf: true) != null) return; // the On desktop switch
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

    CompleteFlow? _completeFlow;

    /// <summary>The steps of completing: crossed out, sliding away, archived (see CompleteFlow).</summary>
    CompleteFlow Flow => _completeFlow ??= new CompleteFlow(CompleteNow);

    /// <summary>Starts completing (with a moment to change one's mind), or cancels if it's already completing.</summary>
    internal void ToggleCompleting(TodoItem item)
    {
        if (Flow.Toggle(item) == CompleteResult.Cancelled) A11y.Announce(this, "Cancelled");
    }

    /// <summary>Archives the item once its row has left.</summary>
    void CompleteNow(TodoItem item)
    {
        Actions.Complete(item);
        ShowUndo($"Completed {FirstLine(item.Text)}");
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

    /// <summary>
    /// A card or category only takes the other's place once the finger is past the middle of it. Swapping as soon
    /// as the finger touches a taller neighbour would put that neighbour right back under the finger, and the two
    /// would swap back and forth.
    /// </summary>
    static bool PastMiddle(int from, int to, Point p, Size size) =>
        to > from ? p.Y > size.Height / 2 : p.Y < size.Height / 2;

    bool MoveBoardOver(object dragged, object target, Point p, Size size)
    {
        var boards = Store.Data.Boards; // only boards can be dragged on the cards screen
        int from = boards.IndexOf((Board)dragged), to = boards.IndexOf((Board)target);
        if (!PastMiddle(from, to, p, size)) return false;
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
                int fromIndex = cats.IndexOf(d), toIndex = cats.IndexOf(t);
                if (!PastMiddle(fromIndex, toIndex, p, size)) return false;
                _categoryFrom ??= fromIndex;
                cats.Move(fromIndex, toIndex);
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

    /// <summary>Shows a list of choices sliding up from the bottom; a tap outside, a swipe down or Back closes it.</summary>
    internal void ShowSheet(string title, params SheetOption[] options) => ShowSheetWithRow(title, null, options);

    /// <summary>A sheet whose first part is something else than a row (the color swatches of a list).</summary>
    void ShowSheetWithRow(string title, Control? first, SheetOption[] options)
    {
        var panel = new StackPanel();
        if (first != null) panel.Children.Add(first);
        foreach (var option in options)
        {
            var row = new SheetRow
            {
                Content = option.Text,
                Icon = option.Icon == null ? null : Application.Current!.FindResource(option.Icon) as Avalonia.Media.Geometry,
                HasSwitch = option.IsOn != null,
                SwitchOn = option.IsOn == true,
            };
            if (option.Danger) row.Classes.Add("danger");
            row.Click += (_, _) => { HideSheet(); option.Run(); };
            panel.Children.Add(row);
        }
        ShowSheetContent(title, panel);
    }

    void ShowSheetContent(string title, Control content)
    {
        SheetTitle.Text = title;
        SheetContent.Content = content;
        SheetLayer.IsVisible = true;
        SlideSheetIn();
    }

    internal void HideSheet()
    {
        SheetLayer.IsVisible = false;
        SheetContent.Content = null;
    }

    void SheetBackground_Tapped(object? sender, TappedEventArgs e) => HideSheet();

    internal void ShowItemMenu(TodoItem item)
    {
        var options = new System.Collections.Generic.List<SheetOption>
        {
            new("Edit", () => BeginEdit(item), Icon: "Line.Rename"),
            new("Copy", () => Copied(Copying.Item(this, item)), Icon: "Line.Copy"),
            new(item.HasDue ? "Change due date" : "Add due date", () => PickDate(item, reminder: false), Icon: "Line.Calendar"),
        };
        if (item.HasDue) options.Add(new("Remove due date", () => Actions.SetDueDate(item, null), Icon: "Line.Calendar"));
        options.Add(new(item.HasPendingReminder ? "Change reminder" : "Add reminder", () => PickDate(item, reminder: true), Icon: "Line.Bell"));
        if (item.HasPendingReminder) options.Add(new("Remove reminder", () => Actions.SetReminder(item, null), Icon: "Line.Bell"));
        if (Store.Data.Boards.Any(b => b != _board && b.Categories.Count > 0))
            options.Add(new("Move to list", () => ShowMoveToBoard(item), Icon: "Line.Folder"));
        options.Add(new("Delete", () => { Actions.Delete(item); ShowUndo($"Deleted {FirstLine(item.Text)}"); }, Danger: true, Icon: "Line.Delete"));
        ShowSheet(FirstLine(item.Text), options.ToArray());
    }

    void ShowMoveToBoard(TodoItem item)
    {
        var targets = Store.Data.Boards.Where(b => b != _board && b.Categories.Count > 0).ToList();
        ShowSheet("Move to list", targets.Select(b =>
        {
            string name = string.IsNullOrWhiteSpace(b.Title) ? "No title" : b.Title;
            return new SheetOption(name, () =>
            {
                var cat = b.Categories[0];
                Actions.MoveItem(item, cat, cat.Items.Count);
                ShowUndo($"Moved to {name}");
            }, Icon: "Line.Folder");
        }).ToArray());
    }

    internal void ShowCategoryMenu(Category cat)
    {
        var options = new System.Collections.Generic.List<SheetOption>
        {
            new("Rename", () => BeginEdit(cat), Icon: "Line.Rename"),
            new("Color", () => ShowSheet("Color", Actions.Palette.Select((hex, i) =>
                new SheetOption(Actions.PaletteNames[i], () => Actions.SetCategoryColor(cat, hex), Icon: "Line.Palette")).ToArray()), Icon: "Line.Palette"),
        };
        if (cat.Items.Count > 0) options.Add(new("Copy items", () => Copied(Copying.Category(this, cat)), Icon: "Line.Copy"));
        options.Add(new("Delete category", () => { Actions.DeleteCategory(cat); ShowUndo($"Deleted category {cat.Name}"); }, Danger: true, Icon: "Line.Delete"));
        ShowSheet(cat.Name, options.ToArray());
    }

    /// <summary>Says in the bar at the bottom how many items were copied (or that copying failed).</summary>
    async void Copied(System.Threading.Tasks.Task<string> copying) => ShowMessage(await copying);

    void BoardMenu_Click(object? sender, RoutedEventArgs e)
    {
        ShowBoardMenu(_board!, fromCard: false);
    }

    internal void ShowBoardMenu(Board board, bool fromCard)
    {
        string name = string.IsNullOrWhiteSpace(board.Title) ? "No title" : board.Title;
        var options = new System.Collections.Generic.List<SheetOption>();
        if (_sync?.IsAvailable == true)
            options.Add(new("Sync this list", () => _ = _sync.SetSynced(board, !board.Synced), Icon: "Line.Sync", IsOn: board.Synced));
        if (CopyText.Count(board) > 0)
            options.Add(new("Copy all items", () => Copied(Copying.Board(this, board)), Icon: "Line.Copy"));
        options.Add(fromCard ? new("Open", () => ShowBoard(board), Icon: "Line.Folder") : new("Rename", BeginTitleEdit, Icon: "Line.Rename"));
        options.Add(new("Delete list", () => ConfirmDeleteBoard(board, name), Danger: true, Icon: "Line.Delete"));
        ShowSheetWithRow(name, ListColorRow(board), options.ToArray());
    }

    /// <summary>"List color": one row of swatches, the current one ringed.</summary>
    Control ListColorRow(Board board)
    {
        var swatches = new WrapPanel { Margin = new Thickness(22, 6, 16, 4) };
        foreach (var style in Themes.BoardStyles)
        {
            var button = new Button
            {
                Content = new Avalonia.Controls.Shapes.Ellipse
                {
                    Fill = Themes.Brush(style.Background),
                    Stroke = Themes.Brush("#40808080"),
                    StrokeThickness = 1,
                },
            };
            button.Classes.Add("swatch");
            button.Classes.Set("selected", style.Key == board.Style);
            Avalonia.Automation.AutomationProperties.SetName(button, style.Name);
            string key = style.Key;
            button.Click += (_, _) => { HideSheet(); SetBoardStyle(board, key); };
            swatches.Children.Add(button);
        }
        var label = new TextBlock { Text = "List color", FontSize = 13, Margin = new Thickness(22, 4, 16, 0) };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("Text3"));
        return new StackPanel { Children = { label, swatches } };
    }

    void SetBoardStyle(Board board, string key)
    {
        board.Style = key; // the card and the open list follow (OnOpenBoardChanged, BoardCard)
        Store.MarkDirty();
    }

    /// <summary>The open list's color changed, also from its widget in the desktop's "All lists".</summary>
    void OnOpenBoardChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Board.Style) && sender is Board b && b == _board) ApplyBoardLook(b);
    }

    void ConfirmDeleteBoard(Board board, string name)
    {
        int items = board.Categories.Sum(c => c.Items.Count);
        var question = items > 0 ? $"Delete \"{name}\" with {items} items? They don't go to the archive." : $"Delete \"{name}\"?";
        if (board.Synced) question += " It will also be deleted from your other devices.";
        ShowSheet(question,
            new SheetOption("Delete", () =>
            {
                Actions.DeleteBoard(board); // asked first, so not undoable
                if (_board == board) ShowHome();
            }, Danger: true, Icon: "Line.Delete"),
            new SheetOption("Cancel", () => { }));
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
        error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable("Danger"));

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
        var matches = Store.Data.Archive.Where(a => q.Length == 0
                     || a.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                     || a.CategoryName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                     || a.BoardTitle.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        _archiveRows.Clear();
        foreach (var row in ArchiveGroups.Rows(matches, DateTime.Now)) _archiveRows.Add(row);
        ArchiveEmpty.IsVisible = Store.Data.Archive.Count == 0;
    }

    void ArchiveItem_Tapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is ArchivedItem a) ShowArchivedMenu(a);
    }

    internal void ShowArchivedMenu(ArchivedItem a) =>
        ShowSheet(FirstLine(a.Text),
            new SheetOption("Restore", () =>
            {
                var done = Actions.Restore(new[] { a });
                if (done.Count == 0) ShowMessage("There is no category to restore into. Add a category to a list first.");
                else if (done[0].category.Id != a.CategoryId)
                    ShowUndo($"\"{a.CategoryName}\" no longer exists, so it went to \"{done[0].category.Name}\"");
                else ShowUndo("Restored");
            }, Icon: "Line.Restore"),
            new SheetOption("Delete permanently", () => { Actions.DeleteArchived(new[] { a }); ShowUndo("Deleted permanently"); }, Danger: true, Icon: "Line.Delete"));

    // ================= Undo bar =================

    Action? _snackbarAction;

    /// <summary>Says what just happened, with an Undo button, for a few seconds.</summary>
    internal void ShowUndo(string message) =>
        ShowSnackbar(message, "Undo", UndoLastChange, message + ". Undo is available.");

    void ShowMessage(string message) => ShowSnackbar(message, null, null, message);

    /// <summary>Messages from PinList.Core (e.g. "the data file was damaged, the backup was loaded").</summary>
    public void ShowMessageForStore(string message) => ShowMessage(message);

    /// <summary>The bar at the bottom, with an optional button (Undo), for a few seconds.</summary>
    void ShowSnackbar(string message, string? button, Action? action, string announcement)
    {
        SnackbarText.Text = message;
        UndoButton.Content = button;
        UndoButton.IsVisible = button != null;
        _snackbarAction = action;
        Snackbar.IsVisible = true;
        _snackTimer.Stop();
        _snackTimer.Start();
        A11y.Announce(this, announcement);
    }

    void HideSnackbar()
    {
        _snackTimer.Stop();
        Snackbar.IsVisible = false;
    }

    void SnackbarButton_Click(object? sender, RoutedEventArgs e)
    {
        var action = _snackbarAction;
        HideSnackbar();
        action?.Invoke();
    }

    void UndoLastChange()
    {
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
            new SheetOption("Add next to mine", () => { Store.Merge(imported); ShowMessage("Imported"); }, Icon: "Line.Import"),
            new SheetOption("Replace everything (a copy of mine is kept)", () =>
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
            }, Danger: true, Icon: "Line.Restore"),
            new SheetOption("Cancel", () => { }));
    }
}
