using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PinList;

/// <summary>
/// A menu entry with a row of color swatches (list color, category color). It is one entry for the keyboard:
/// Left and Right move along the swatches, Enter or Space picks the highlighted one. A click on a swatch picks it.
/// The current color has a ring in the accent color. A screen reader hears the label, the color name and the position.
/// </summary>
public sealed class SwatchMenuItem : MenuItem
{
    readonly string _label;
    readonly IReadOnlyList<(string Name, string Hex)> _colors;
    readonly Action<string> _pick;
    readonly List<Border> _rings = new();
    int _highlighted;

    public SwatchMenuItem(string label, IReadOnlyList<(string Name, string Hex)> colors, string? selectedHex, Action<string> pick)
    {
        _label = label;
        _colors = colors;
        _pick = pick;
        int selected = colors.Select(c => c.Hex).ToList().FindIndex(h => string.Equals(h, selectedHex, StringComparison.OrdinalIgnoreCase));
        _highlighted = Math.Max(0, selected);
        Classes.Add("swatches");

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
        for (int i = 0; i < colors.Count; i++)
        {
            int index = i;
            var ring = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                Child = new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = Themes.Brush(colors[i].Hex),
                    Stroke = Themes.Brush("#40808080"),
                    StrokeThickness = 1,
                },
            };
            ToolTip.SetTip(ring, colors[i].Name);
            // pressing a swatch highlights it; the menu entry's own click then closes the menu and picks it
            ring.AddHandler(PointerPressedEvent, (_, _) => Highlight(index), handledEventsToo: true);
            _rings.Add(ring);
            row.Children.Add(ring);
        }
        var text = new TextBlock { Text = label, FontSize = 12 };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Text3"));
        Header = new StackPanel { Children = { text, row } };

        Click += (_, _) => Dispatcher.UIThread.Post(() => _pick(HighlightedHex), DispatcherPriority.Background);
        Refresh();
    }

    protected override Type StyleKeyOverride => typeof(MenuItem);

    /// <summary>The color that Enter would pick now (for tests).</summary>
    public string HighlightedHex => _colors[_highlighted].Hex;

    void Highlight(int index)
    {
        _highlighted = index;
        Refresh();
    }

    void Refresh()
    {
        for (int i = 0; i < _rings.Count; i++)
        {
            if (i == _highlighted) _rings[i].Bind(Border.BorderBrushProperty, _rings[i].GetResourceObservable("Accent"));
            else _rings[i].BorderBrush = Brushes.Transparent;
        }
        AutomationProperties.SetName(this, $"{_label}, {_colors[_highlighted].Name}, {_highlighted + 1} of {_colors.Count}");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Left or Key.Right)
        {
            Highlight(Math.Clamp(_highlighted + (e.Key == Key.Left ? -1 : 1), 0, _colors.Count - 1));
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None)
        {
            // like Enter, which MenuItem handles itself: click the entry and close the menu
            e.Handled = true;
            RaiseEvent(new RoutedEventArgs(ClickEvent));
            this.FindAncestorOfType<ContextMenu>()!.Close();
            return;
        }
        base.OnKeyDown(e);
    }
}
