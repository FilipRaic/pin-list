using System.Diagnostics.CodeAnalysis;
using Android.App;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace PinList.Android;

/// <summary>
/// The look of the small screens a home screen widget opens over the launcher (the add box and the list chooser):
/// a rounded card in the app's own sheet colors and theme, like the menus inside PinList.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Builds native Android views.")]
static class WidgetSheet
{
    /// <summary>The app's sheet colors, in the theme chosen in PinList's Settings (or the phone's when it follows the phone).</summary>
    public static WidgetChrome Chrome(Activity activity)
    {
        bool systemDark = (activity.Resources!.Configuration!.UiMode & UiMode.NightMask) == UiMode.NightYes;
        string? setting = WidgetData.Run(activity, false, () => Store.Data.Settings.Theme);
        return WidgetModel.Chrome(setting, systemDark);
    }

    public static int Dp(Activity activity, float dp) => (int)(dp * activity.Resources!.DisplayMetrics!.Density + 0.5f);

    public static Color Color(uint argb) => new(unchecked((int)argb));

    /// <summary>A rounded shape in one color, with an outline if a width is given.</summary>
    public static GradientDrawable Rounded(Activity activity, uint fill, float radiusDp, uint stroke = 0, float strokeDp = 0)
    {
        var shape = new GradientDrawable();
        shape.SetShape(ShapeType.Rectangle);
        shape.SetCornerRadius(Dp(activity, radiusDp));
        shape.SetColor(Color(fill).ToArgb());
        if (strokeDp > 0) shape.SetStroke(Dp(activity, strokeDp), Color(stroke));
        return shape;
    }

    /// <summary>The card: a column on the app's sheet color with round corners. The window around it is see-through.</summary>
    public static LinearLayout Card(Activity activity, WidgetChrome chrome)
    {
        var card = new LinearLayout(activity) { Orientation = global::Android.Widget.Orientation.Vertical };
        card.Background = Rounded(activity, chrome.Background, 20);
        card.SetPadding(Dp(activity, 20), Dp(activity, 18), Dp(activity, 20), Dp(activity, 16));
        return card;
    }

    public static TextView Text(Activity activity, string text, float sizeSp, uint color, bool bold = false)
    {
        var view = new TextView(activity) { Text = text };
        view.SetTextSize(ComplexUnitType.Sp, sizeSp);
        view.SetTextColor(Color(color));
        if (bold) view.SetTypeface(view.Typeface, TypefaceStyle.Bold);
        return view;
    }

    /// <summary>Puts the card on the screen with a little room around it.</summary>
    public static void Show(Activity activity, View card)
    {
        var frame = new FrameLayout(activity);
        int gap = Dp(activity, 12);
        frame.SetPadding(gap, gap, gap, gap);
        frame.AddView(card, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        activity.SetContentView(frame);
    }
}
