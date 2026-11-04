using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace PinList;

/// <summary>The sections of the PinList window.</summary>
public enum MainSection { Lists, Archive, Settings }

/// <summary>
/// The PinList window on the desktop: a sidebar with All lists, Archive and Settings, and the section on its right.
/// "All lists" is the phone's cards screen, the archive and the settings are shared with the phone too.
/// Closing it only hides it, so it comes back at the same size and place.
/// </summary>
public partial class MainWindow : Window
{
    public PhoneView Lists { get; }
    public ArchiveView Archive { get; }
    public SettingsView Settings { get; }

    public MainSection Section { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Look.GrayscaleText(this);
        Lists = new PhoneView(desktop: true);
        Archive = new ArchiveView();
        var settingsHost = new DesktopSettingsHost(App.Instance);
        Settings = new SettingsView(settingsHost);
        Sections.Children.Add(Lists);
        Sections.Children.Add(Archive);
        Sections.Children.Add(Settings);

        Closing += (_, e) =>
        {
            // a Windows shutdown or Quit on a Mac can come before PinList heard about it: never say no to it
            if (BoardWindow.IsShutdown(e.CloseReason)) App.ShuttingDown = true;
            if (App.ShuttingDown) return;
            e.Cancel = true;
            Hide();
        };
        App.ThemeChanged += ApplyLook;
        Closed += (_, _) =>
        {
            App.ThemeChanged -= ApplyLook;
            settingsHost.Dispose();
            Store.Data.Boards.CollectionChanged -= OnCountsChanged;
            Store.Data.Archive.CollectionChanged -= OnCountsChanged;
        };
        Opened += (_, _) => StyleTitleBar();
        Store.Data.Boards.CollectionChanged += OnCountsChanged;
        Store.Data.Archive.CollectionChanged += OnCountsChanged;

        ApplyLook();
        ShowSection(MainSection.Lists);
        UpdateCounts();
    }

    /// <summary>Shows one section and marks it in the sidebar.</summary>
    public void ShowSection(MainSection section)
    {
        Section = section;
        Lists.IsVisible = section == MainSection.Lists;
        Archive.IsVisible = section == MainSection.Archive;
        Settings.IsVisible = section == MainSection.Settings;
        NavLists.Classes.Set("selected", section == MainSection.Lists);
        NavArchive.Classes.Set("selected", section == MainSection.Archive);
        NavSettings.Classes.Set("selected", section == MainSection.Settings);
        if (section == MainSection.Archive) Archive.FocusStart();
        if (section == MainSection.Settings) Settings.Refresh();
        if (section == MainSection.Lists && Store.Data.Boards.Count == 0 && IsVisible) Lists.FocusNewList();
    }

    void Nav_Click(object? sender, RoutedEventArgs e) =>
        ShowSection(Enum.Parse<MainSection>((string)((Control)sender!).Tag!));

    /// <summary>The data was replaced (an import) or the lists changed: everything shows the current data.</summary>
    internal void Reload()
    {
        Store.Data.Boards.CollectionChanged -= OnCountsChanged;
        Store.Data.Archive.CollectionChanged -= OnCountsChanged;
        Store.Data.Boards.CollectionChanged += OnCountsChanged;
        Store.Data.Archive.CollectionChanged += OnCountsChanged;
        Lists.Reload();
        Archive.Reload();
        Settings.Refresh();
        UpdateCounts();
    }

    void OnCountsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateCounts();

    void UpdateCounts()
    {
        ListsCount.Text = Store.Data.Boards.Count.ToString();
        ArchiveCount.Text = Store.Data.Archive.Count.ToString();
    }

    /// <summary>Colors and text size (after a theme, contrast or text size change).</summary>
    internal void RefreshAppearance()
    {
        ApplyLook();
    }

    void ApplyLook()
    {
        Scaler.LayoutTransform = A11y.TextScale == 1.0 ? null : new ScaleTransform(A11y.TextScale, A11y.TextScale);
        ChromeLook.Apply(this);
        Lists.App_ThemeOrDataChanged();
        StyleTitleBar();
    }

    void StyleTitleBar()
    {
        var bg = ((ISolidColorBrush)this.FindResource("Bg")!).Color;
        Native.StyleTitleBar(this, dark: Themes.IsDark(Store.Data.Settings.Theme), bg);
    }
}
