using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace PinList;

/// <summary>
/// PinList's own notification in the top right corner of the screen, for macOS when it doesn't allow PinList's
/// notifications. Clicking it runs its action and closes it, the × only closes it. It stays until it's clicked or
/// closed. Several are stacked under each other.
/// </summary>
static class ReminderPopup
{
    const double Width = 320;
    const double Margin = 16;
    const double Gap = 8;

    static readonly List<Window> Open = new();

    /// <summary>The notifications on the screen now, top first (tests look at them).</summary>
    internal static IReadOnlyList<Window> Shown => Open;

    /// <summary>Shows a notification. It stays until the user clicks or closes it.</summary>
    public static Window Show(string title, string text, Action onClick)
    {
        var body = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 28, 0) };
        body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14 });
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13 });

        var close = new Button
        {
            Content = new LineIcon { Data = (Geometry)Application.Current!.FindResource("Line.Close")! },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -4, -8, 0),
        };
        close.Classes.Add("icon");
        AutomationProperties.SetName(close, "Close");
        ToolTip.SetTip(close, "Close");

        var content = new Panel();
        content.Children.Add(body);
        content.Children.Add(close);

        var border = new Border
        {
            CornerRadius = (CornerRadius)Application.Current!.FindResource("R.Menu")!,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 12),
            Child = content,
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("Raised"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Line"));
        body.Bind(TextBlock.ForegroundProperty, body.GetResourceObservable("Text1"));
        close.Bind(Button.ForegroundProperty, close.GetResourceObservable("Text3"));

        var w = new Window
        {
            WindowDecorations = WindowDecorations.None,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            ShowInTaskbar = false,
            ShowActivated = false, // never takes the keyboard from what the user is typing in
            Topmost = true,
            CanResize = false,
            Width = Width,
            SizeToContent = SizeToContent.Height,
            Content = border,
            Title = title,
        };
        AutomationProperties.SetHelpText(w, text);
        Look.GrayscaleText(w);

        // the × closes it; a click anywhere else runs the action
        close.Click += (_, e) => { e.Handled = true; w.Close(); };
        w.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) == close) return;
            w.Close();
            onClick();
        };
        w.Closed += (_, _) =>
        {
            Open.Remove(w);
            Arrange();
        };
        w.Opened += (_, _) => Native.MakeToolWindow(w);
        w.SizeChanged += (_, _) => Arrange();

        Open.Add(w);
        Arrange();
        w.Show();
        return w;
    }

    /// <summary>Stacks the notifications from the top right corner down, in the order they came.</summary>
    static void Arrange()
    {
        var wa = Displays.Primary;
        double top = wa.Top + Margin;
        foreach (var w in Open.ToList())
        {
            w.Position = Displays.ToPixels(wa.Right - Width - Margin, top);
            double height = w.Bounds.Height > 0 ? w.Bounds.Height : 72;
            top += height + Gap;
        }
    }
}
