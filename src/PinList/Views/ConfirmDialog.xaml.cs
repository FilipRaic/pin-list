using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PinList;

public enum DialogButtonKind { Normal, Accent, Danger }

/// <summary>Dialog styled like the app's menus (instead of the Windows MessageBox).</summary>
public partial class ConfirmDialog : Window
{
    int _choice = -1;

    ConfirmDialog(string title, string message, (string text, DialogButtonKind kind)[] buttons, int defaultIndex)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        // screen readers announce the window title and its description when the dialog opens
        Title = title;
        System.Windows.Automation.AutomationProperties.SetHelpText(this, message);
        A11y.ApplyTextScale(this);
        Width *= A11y.TextScale;
        MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;

        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var (text, kind) = buttons[i];
            var b = new Button
            {
                Content = text,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                Style = (Style)FindResource(kind switch
                {
                    DialogButtonKind.Accent => "AccentButton",
                    DialogButtonKind.Danger => "DangerButton",
                    _ => "DialogButton",
                }),
                IsDefault = i == defaultIndex,
            };
            b.Click += (_, _) => { _choice = index; DialogResult = true; };
            Buttons.Children.Add(b);
            // keyboard focus starts on the default button (Tab / arrow keys reach the others)
            if (i == defaultIndex) Loaded += (_, _) => b.Focus();
        }

        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        SourceInitialized += (_, _) => Native.MakeToolWindow(this);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }

    /// <summary>Shows the dialog; returns the index of the clicked button, or -1 for Esc.</summary>
    public static int Choose(Window? owner, string title, string message, int defaultIndex,
                             params (string text, DialogButtonKind kind)[] buttons)
    {
        var dlg = new ConfirmDialog(title, message, buttons, defaultIndex);
        if (owner != null && owner.IsVisible) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg._choice;
    }
}
