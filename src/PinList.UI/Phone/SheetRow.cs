using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PinList;

/// <summary>
/// One row of a bottom sheet: a line icon, the text (the button's Content, which is also its name) and, for a
/// setting, a switch on the right. The template is in PhoneView.axaml. Content stays plain text.
/// </summary>
public class SheetRow : Button
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<SheetRow, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<bool> HasSwitchProperty =
        AvaloniaProperty.Register<SheetRow, bool>(nameof(HasSwitch));

    public static readonly StyledProperty<bool> SwitchOnProperty =
        AvaloniaProperty.Register<SheetRow, bool>(nameof(SwitchOn));

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>The row shows a switch on its right (a setting that is on or off).</summary>
    public bool HasSwitch
    {
        get => GetValue(HasSwitchProperty);
        set => SetValue(HasSwitchProperty, value);
    }

    public bool SwitchOn
    {
        get => GetValue(SwitchOnProperty);
        set => SetValue(SwitchOnProperty, value);
    }
}
