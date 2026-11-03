using Avalonia;

namespace PinList;

/// <summary>
/// Tells the shared templates (archive rows) which list style the chrome (the PinList window, the phone screens)
/// looks like, so category colors get a readable shade ("Dark" or "Light"), and a counter that changes whenever the
/// colors are applied again (a theme or contrast change). Both are set once on a window or screen and inherited.
/// </summary>
public static class ChromeLook
{
    public static readonly AttachedProperty<string> StyleKeyProperty =
        AvaloniaProperty.RegisterAttached<Visual, Visual, string>("StyleKey", "Dark", inherits: true);

    public static readonly AttachedProperty<int> VersionProperty =
        AvaloniaProperty.RegisterAttached<Visual, Visual, int>("Version", 0, inherits: true);

    public static string GetStyleKey(Visual v) => v.GetValue(StyleKeyProperty);
    public static void SetStyleKey(Visual v, string value) => v.SetValue(StyleKeyProperty, value);
    public static int GetVersion(Visual v) => v.GetValue(VersionProperty);
    public static void SetVersion(Visual v, int value) => v.SetValue(VersionProperty, value);

    /// <summary>Sets the style for the current app theme and counts one more time the colors were applied.</summary>
    public static void Apply(Visual root)
    {
        SetStyleKey(root, Themes.IsDark(Store.Data.Settings.Theme) ? "Dark" : "Light");
        SetVersion(root, GetVersion(root) + 1);
    }
}
