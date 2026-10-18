using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace PinList;

public partial class App : Application
{
    public static App Instance => (App)Current;
    public static bool ShuttingDown { get; private set; }

    Mutex? _mutex;
    WinForms.NotifyIcon? _tray;
    Window? _menuHost;
    ArchiveWindow? _archive;
    readonly List<BoardWindow> _windows = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single instance only
        _mutex = new Mutex(true, @"Local\PinList.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            ShuttingDown = true;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            Store.Log("Error: " + ex.Exception);
            ex.Handled = true;
        };

        ApplyTheme();

        if (!Store.Load())
        {
            // First run: one empty widget, start with Windows
            Store.Data.Boards.Add(Store.DefaultBoard());
            Autostart.Set(true);
            Store.SaveNow();
        }
        if (Store.Data.Boards.Count == 0)
        {
            Store.Data.Boards.Add(Store.DefaultBoard());
            Store.SaveNow();
        }
        Autostart.RefreshPath();

        foreach (var board in Store.Data.Boards.ToList())
            OpenBoard(board);

        CreateTray();

        SessionEnding += (_, _) => { ShuttingDown = true; Store.Flush(); };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ShuttingDown = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Store.Flush();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            foreach (var w in _windows.ToList()) w.EnsureOnScreen();
        });

    void ApplyTheme()
    {
        Themes.ApplyBoard(Resources, Themes.Get("Dark"), true); // defaults; every widget overrides these
        Themes.ApplyChrome(Resources);
    }

    public void ExitApp()
    {
        ShuttingDown = true;
        Store.Flush();
        Shutdown();
    }

    // ---- Windows ----

    public void OpenBoard(Board board)
    {
        var w = new BoardWindow(board);
        w.Closed += (_, _) =>
        {
            _windows.Remove(w);
            // When Explorer restarts, Windows destroys our widgets too (their owner is the desktop).
            // Reopen them; the data is still in memory and on disk.
            if (!w.UserDeleted && !ShuttingDown && Store.Data.Boards.Contains(board))
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    if (!ShuttingDown && Store.Data.Boards.Contains(board) && _windows.All(x => x.Board != board))
                        OpenBoard(board);
                };
                t.Start();
            }
        };
        _windows.Add(w);
        w.Show();
    }

    public void ShowAllBoards()
    {
        foreach (var w in _windows.ToList())
        {
            if (!w.IsVisible) w.Show();
            w.Topmost = true;   // briefly on top so it shows above other apps
            w.Topmost = false;
        }
        _windows.LastOrDefault()?.Activate();
    }

    public void ShowArchive()
    {
        if (_archive == null)
        {
            _archive = new ArchiveWindow();
            _archive.Closed += (_, _) => _archive = null;
            _archive.Show();
        }
        else
        {
            if (_archive.WindowState == WindowState.Minimized) _archive.WindowState = WindowState.Normal;
            _archive.Activate();
        }
    }

    // ---- Tray icon ----

    void CreateTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "PinList",
            Visible = true,
        };
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) ShowAllBoards();
            else if (e.Button == WinForms.MouseButtons.Right) ShowTrayMenu();
        };
    }

    /// <summary>The tray menu is a WPF ContextMenu, so it looks like every other menu in the app.</summary>
    void ShowTrayMenu()
    {
        // Invisible host window: the menu only closes on an outside click if the app is in the foreground
        if (_menuHost == null)
        {
            _menuHost = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false,
                Width = 1, Height = 1, Left = -32000, Top = -32000,
            };
            _menuHost.SourceInitialized += (_, _) => Native.MakeToolWindow(_menuHost);
            _menuHost.Show();
        }
        _menuHost.Activate();
        Native.BringToForeground(_menuHost);

        var menu = new ContextMenu { PlacementTarget = _menuHost, Placement = PlacementMode.MousePoint };
        menu.Items.Add(CreateMenuItem("New window", () => Actions.NewBoard(_windows.LastOrDefault()?.Board)));
        menu.Items.Add(CreateMenuItem("Archive", ShowArchive));
        menu.Items.Add(CreateMenuItem("Show all windows", ShowAllBoards));
        menu.Items.Add(new Separator());
        var auto = CreateMenuItem("Start with Windows", () => Autostart.Set(!Autostart.IsEnabled()));
        auto.IsChecked = Autostart.IsEnabled();
        menu.Items.Add(auto);
        menu.Items.Add(CreateMenuItem("Open data folder", () =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Store.Dir}\"") { UseShellExecute = true })));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Exit", ExitApp));
        menu.IsOpen = true;
    }

    public static MenuItem CreateMenuItem(string header, Action action, string? gesture = null)
    {
        var mi = new MenuItem { Header = header };
        if (gesture != null) mi.InputGestureText = gesture;
        // deferred so the menu closes before the action runs (e.g. before a TextBox takes focus)
        mi.Click += (_, _) => Current.Dispatcher.InvokeAsync(action, DispatcherPriority.ApplicationIdle);
        return mi;
    }

    static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var info = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (info != null)
            {
                using var s = info.Stream;
                return new System.Drawing.Icon(s, WinForms.SystemInformation.SmallIconSize);
            }
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }
}
