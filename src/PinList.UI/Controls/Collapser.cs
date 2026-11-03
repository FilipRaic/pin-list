using Avalonia;
using Avalonia.Controls;

namespace PinList;

/// <summary>
/// Wraps one row, or the items of a category. <see cref="Reveal"/> goes from 1 (full height) to 0 (no height), so when
/// a completed row slides away or a category collapses, the rows below glide instead of jumping. The content is
/// clipped while it shrinks.
/// </summary>
public class Collapser : Decorator
{
    public static readonly StyledProperty<double> RevealProperty =
        AvaloniaProperty.Register<Collapser, double>(nameof(Reveal), 1, coerce: (_, v) => System.Math.Clamp(v, 0, 1));

    static Collapser()
    {
        AffectsMeasure<Collapser>(RevealProperty);
        ClipToBoundsProperty.OverrideDefaultValue<Collapser>(true);
    }

    /// <summary>
    /// The content is hidden once it is fully closed, so the keyboard and screen readers skip it (a collapsed
    /// category). It shows again as soon as it starts to open.
    /// </summary>
    public bool HidesWhenClosed { get; set; }

    /// <summary>How much of the row's height is shown, 0 to 1.</summary>
    public double Reveal
    {
        get => GetValue(RevealProperty);
        set => SetValue(RevealProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (HidesWhenClosed && (change.Property == RevealProperty || change.Property == ChildProperty) && Child is { } child)
            child.IsVisible = Reveal > 0;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child) return default;
        child.Measure(availableSize);
        return new Size(child.DesiredSize.Width, child.DesiredSize.Height * Reveal);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // the child keeps its full height (it is clipped), only this control shrinks
        if (Child is { } child) child.Arrange(new Rect(0, 0, finalSize.Width, child.DesiredSize.Height));
        return finalSize;
    }
}
