using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace PinList;

/// <summary>
/// A small reminder notification in the corner of the screen, for platforms where PinList shows its own
/// (macOS without a signed app bundle). Clicking it shows the item; it goes away after 10 seconds.
/// </summary>
static class ReminderPopup
{
    public static Window Show(string title, string text, Action onClick)
    {
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14 });
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13 });

        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 12),
            Child = body,
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("MenuBg"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("MenuBorder"));
        body.Bind(TextBlock.ForegroundProperty, body.GetResourceObservable("MenuText"));

        var w = new Window
        {
            WindowDecorations = WindowDecorations.None,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            ShowInTaskbar = false,
            Topmost = true,
            CanResize = false,
            Width = 320,
            SizeToContent = SizeToContent.Height,
            Content = border,
            Title = title,
        };
        Look.GrayscaleText(w);
        var wa = Displays.Primary;
        w.Position = Displays.ToPixels(wa.Right - 320 - 16, wa.Top + 16);
        w.PointerPressed += (_, _) => { w.Close(); onClick(); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) => { timer.Stop(); w.Close(); };
        w.Opened += (_, _) => timer.Start();
        w.Show();
        return w;
    }
}
