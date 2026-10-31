namespace PinList;

/// <summary>The usable part of a screen (without taskbar or menu bar), in the app's own units.</summary>
public readonly record struct WorkArea(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}
