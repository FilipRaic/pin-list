using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PinList;

/// <summary>
/// "All lists": every list as a card, the same screen as the phone's first one, in an ordinary window. Each card has
/// a Widget switch, so a list can live here only, without a widget on the desktop, and still be opened and edited.
/// Closing the window only hides it.
/// </summary>
public sealed class ListsWindow : Window
{
    public PhoneView View { get; }

    public ListsWindow()
    {
        Title = "PinList";
        Icon = new WindowIcon(AssetLoader.Open(new System.Uri("avares://PinList/Assets/app.ico")));
        Width = 420;
        Height = 640;
        MinWidth = 320;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this[!BackgroundProperty] = new DynamicResourceExtension("ArcBg");
        View = new PhoneView(desktop: true);
        Content = View;
        Closing += (_, e) =>
        {
            // a Windows shutdown or Quit on a Mac can come before PinList heard about it: never say no to it
            if (BoardWindow.IsShutdown(e.CloseReason)) App.ShuttingDown = true;
            if (App.ShuttingDown) return;
            e.Cancel = true;
            Hide();
        };
    }
}
