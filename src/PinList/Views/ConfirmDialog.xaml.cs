using System.Windows;
using System.Windows.Input;

namespace PinList;

/// <summary>Confirmation dialog styled like the app's menus (instead of the Windows MessageBox).</summary>
public partial class ConfirmDialog : Window
{
    ConfirmDialog(string title, string message, string okText)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = okText;
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        SourceInitialized += (_, _) => Native.MakeToolWindow(this);
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    public static bool Ask(Window? owner, string title, string message, string okText)
    {
        var dlg = new ConfirmDialog(title, message, okText);
        if (owner != null && owner.IsVisible) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dlg.ShowDialog() == true;
    }
}
