using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PinList;

/// <summary>
/// Makes rows slide to their new places when the list is reordered (instead of jumping): remember where every
/// row was, let the list change, then start each moved row at its old place and animate it to the new one.
/// Rows are controls with a Tag (the item, category or board they show) for which <c>isRow</c> is true;
/// the slide itself is the row's RenderTransform transition (see the row styles).
/// </summary>
public static class Reorder
{
    public static Func<Control, bool> DefaultIsRow { get; set; } = c => c is AccessibleRow;

    /// <summary>Where all rows are drawn now (including a slide that is still running), by the object they show.</summary>
    public static Dictionary<object, Point> Capture(Visual root, Func<Control, bool>? isRow = null)
    {
        var map = new Dictionary<object, Point>(ReferenceEqualityComparer.Instance);
        foreach (var row in Rows(root, isRow ?? DefaultIsRow))
            if (row.TranslatePoint(default, root) is { } p) map[row.Tag!] = p;
        return map;
    }

    /// <summary>After the list changed: rows that moved slide from where they were (once the layout has caught up).</summary>
    public static void Animate(Visual root, Dictionary<object, Point> before, Func<Control, bool>? isRow = null) =>
        Dispatcher.UIThread.Post(() => SlideNow(root, before, isRow), DispatcherPriority.Loaded);

    /// <summary>
    /// Same as <see cref="Animate"/>, but right away (the caller has already updated the layout).
    /// The row showing <paramref name="skip"/> is left alone.
    /// </summary>
    public static void SlideNow(Visual root, Dictionary<object, Point> before, Func<Control, bool>? isRow = null,
                                object? skip = null)
    {
        foreach (var row in Rows(root, isRow ?? DefaultIsRow))
        {
            if (ReferenceEquals(row.Tag, skip)) continue;
            if (!before.TryGetValue(row.Tag!, out var old) || LayoutPosition(row, root) is not { } now) continue;
            SlideFrom(row, old.X - now.X, old.Y - now.Y);
        }
    }

    /// <summary>Draws the row moved by (dx, dy) from its place (and scaled), then lets it slide home.</summary>
    public static void SlideFrom(Control row, double dx, double dy, double scale = 1)
    {
        if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5 && scale == 1) return;

        // jump to the old place without animating, then let the transition (from the row style) bring it home
        row.Transitions = null;
        var move = TransformOperations.CreateBuilder(2);
        if (scale != 1) move.AppendScale(scale, scale); // scale first, so the offset itself isn't scaled
        move.AppendTranslate(dx, dy);
        row.RenderTransform = move.Build();
        Dispatcher.UIThread.Post(() =>
        {
            row.ClearValue(Animatable.TransitionsProperty);
            row.ClearValue(Visual.RenderTransformProperty);
        }, DispatcherPriority.Render);
    }

    /// <summary>Where the layout puts the row (without its own slide or lift), relative to root.</summary>
    public static Point? LayoutPosition(Control row, Visual root) =>
        row.GetVisualParent() is { } parent ? parent.TranslatePoint(row.Bounds.Position, root) : null;

    static IEnumerable<Control> Rows(Visual root, Func<Control, bool> isRow) =>
        root.GetVisualDescendants().OfType<Control>().Where(c => c.Tag != null && isRow(c));
}
