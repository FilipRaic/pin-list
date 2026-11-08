using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace PinList;

/// <summary>Dialog styled like the app's menus (instead of the system message box).</summary>
public partial class ConfirmDialog : Window
{
    int _choice = -1;

    /// <summary>For the XAML previewer only.</summary>
    public ConfirmDialog() : this("", "", Array.Empty<(string, DialogButtonKind)>(), 0) { }

    internal ConfirmDialog(string title, string message, (string text, DialogButtonKind kind)[] buttons, int defaultIndex,
                           Control? extra = null)
    {
        InitializeComponent();
        Look.GrayscaleText(this);
        TitleText.Text = title;
        MessageText.Text = message;
        // screen readers announce the window title and its description when the dialog opens
        Title = title;
        AutomationProperties.SetHelpText(this, message);
        if (A11y.TextScale != 1.0) Scaler.LayoutTransform = new ScaleTransform(A11y.TextScale, A11y.TextScale);
        if (extra != null)
        {
            Width = 460; // room for a longer text
            Extra.Content = extra;
            Extra.IsVisible = true;
        }
        Width *= A11y.TextScale;
        MessageText.IsVisible = !string.IsNullOrEmpty(message);

        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var (text, kind) = buttons[i];
            var b = new Button
            {
                Content = text,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                IsDefault = i == defaultIndex,
            };
            b.Classes.Add("dialog");
            if (kind == DialogButtonKind.Accent) b.Classes.Add("accent");
            if (kind == DialogButtonKind.Danger) b.Classes.Add("danger");
            b.Click += (_, _) => { _choice = index; Close(); };
            Buttons.Children.Add(b);
            // keyboard focus starts on the default button (Tab / arrow keys reach the others)
            if (i == defaultIndex) Opened += (_, _) => b.Focus(NavigationMethod.Tab);
        }

        PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        Opened += (_, _) => Native.MakeToolWindow(this);
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { _choice = -1; Close(); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }

    /// <summary>The buttons (tests click them).</summary>
    internal Panel ButtonPanel => Buttons;

    /// <summary>What's below the message, if anything (tests look at it).</summary>
    internal Control? ExtraContent => Extra.Content as Control;

    /// <summary>Shows the dialog; returns the index of the clicked button, or -1 for Esc.</summary>
    public static async Task<int> Choose(Window? owner, string title, string message, int defaultIndex,
                                         params (string text, DialogButtonKind kind)[] buttons)
    {
        var dlg = new ConfirmDialog(title, message, buttons, defaultIndex);
        await DialogHost.Run(dlg, owner);
        return dlg._choice;
    }

    /// <summary>A new version and what's new in it. Returns true for Get it, false for Later or Esc.</summary>
    public static async Task<bool> ShowUpdate(Window? owner, UpdateInfo update)
    {
        var notes = ReleaseNotesView.Build(update.Notes, 13);
        var extra = notes == null ? null : new ScrollViewer
        {
            Content = notes,
            MaxHeight = 340,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 12, 0), // room for the scroll bar
        };
        var dlg = new ConfirmDialog($"PinList {update.Version} is available",
            notes == null ? "Get it opens the download page." : "What's new:",
            new[] { ("Later", DialogButtonKind.Normal), ("Get it", DialogButtonKind.Accent) }, 1, extra);
        await DialogHost.Run(dlg, owner);
        return dlg._choice == 1;
    }
}

/// <summary>Shows a dialog over its owner, or in the middle of the screen when there is none.</summary>
static class DialogHost
{
    /// <summary>The dialog just shown (tests answer it).</summary>
    internal static Window? Current { get; private set; }

    public static Task Run(Window dlg, Window? owner)
    {
        Current = dlg;
        dlg.Closed += (_, _) => { if (Current == dlg) Current = null; };
        if (owner != null && owner.IsVisible) return dlg.ShowDialog(owner);

        dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var done = new TaskCompletionSource();
        dlg.Closed += (_, _) => done.TrySetResult();
        dlg.Show();
        return done.Task;
    }
}
