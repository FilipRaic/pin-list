using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace PinList;

/// <summary>
/// A line icon: a geometry drawn as a stroke (about 1.7, round caps and joins) in the inherited foreground color.
/// The geometries (the Line.* resources) are drawn on a 24 x 24 grid and scaled to the size of the control.
/// </summary>
public class LineIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<LineIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<LineIcon, double>(nameof(StrokeThickness), 1.7);

    static LineIcon()
    {
        AffectsRender<LineIcon>(DataProperty, StrokeThicknessProperty, TextElement.ForegroundProperty);
        AffectsMeasure<LineIcon>(DataProperty);
    }

    public LineIcon()
    {
        Width = 16;
        Height = 16;
        IsHitTestVisible = false;
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Stroke width on the 24 x 24 grid.</summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is not { } geometry || TextElement.GetForeground(this) is not { } brush) return;
        double scale = System.Math.Min(Bounds.Width, Bounds.Height) / 24;
        if (scale <= 0) return;
        var pen = new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            context.DrawGeometry(null, pen, geometry);
    }
}
