using System.Diagnostics.CodeAnalysis;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;

namespace PinList.Android;

/// <summary>
/// Asks which list a home screen widget shows: when the widget is placed, when the launcher's "reconfigure" is
/// used (Android 12 and newer), and from the widget itself after its list was deleted. It looks like the app's
/// own sheets (see <see cref="WidgetSheet"/>).
/// </summary>
[Register("com.pinlist.app.ChooseListActivity")]
[Activity(Label = "Choose a list", Theme = "@style/PinListDialogTheme", Exported = true, ExcludeFromRecents = true,
    TaskAffinity = "")]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetConfigure })]
[ExcludeFromCodeCoverage(Justification = "A native Android screen.")]
public class ChooseListActivity : AppCompatActivity
{
    int _widgetId = AppWidgetManager.InvalidAppwidgetId;
    WidgetChrome _chrome = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _widgetId = Intent?.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId)
                    ?? AppWidgetManager.InvalidAppwidgetId;
        // Leaving without choosing tells the launcher not to place the widget
        SetResult(Result.Canceled, new Intent().PutExtra(AppWidgetManager.ExtraAppwidgetId, _widgetId));
        if (_widgetId == AppWidgetManager.InvalidAppwidgetId)
        {
            Finish();
            return;
        }

        _chrome = WidgetSheet.Chrome(this);
        var choices = WidgetData.Run(this, false, () => WidgetModel.Choices(Store.Data));
        var card = WidgetSheet.Card(this, _chrome);
        var title = choices.Count > 0
            ? WidgetSheet.Text(this, WidgetModel.ChooseText, 17, _chrome.Text, bold: true)
            : WidgetSheet.Text(this, WidgetModel.NoListsText, 15, _chrome.Text);
        title.SetPadding(0, 0, 0, Dp(8));
        card.AddView(title);

        foreach (var choice in choices) card.AddView(Row(choice));
        if (choices.Count == 0)
        {
            var open = WidgetSheet.Text(this, "Open PinList", 15, _chrome.AccentText, bold: true);
            open.Background = WidgetSheet.Rounded(this, _chrome.Accent, 12);
            open.Gravity = GravityFlags.Center;
            open.SetPadding(Dp(16), Dp(12), Dp(16), Dp(12));
            open.Clickable = true;
            open.Focusable = true;
            open.Click += (_, _) =>
            {
                StartActivity(new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask));
                Finish();
            };
            card.AddView(open, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = Dp(8),
            });
        }

        // many lists: the card scrolls inside the window
        var scroll = new ScrollView(this);
        scroll.AddView(card);
        WidgetSheet.Show(this, scroll);
    }

    View Row(WidgetChoice choice)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal, Clickable = true, Focusable = true };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetMinimumHeight(Dp(52));
        var ripple = new TypedValue();
        if (Theme!.ResolveAttribute(global::Android.Resource.Attribute.SelectableItemBackground, ripple, true))
            row.SetBackgroundResource(ripple.ResourceId);

        // the list's color, with a thin outline so it also shows when it is close to the card's own color
        row.AddView(new View(this) { Background = WidgetSheet.Rounded(this, choice.Swatch, 6, _chrome.Muted, 1) },
            new LinearLayout.LayoutParams(Dp(22), Dp(22)));
        row.AddView(WidgetSheet.Text(this, choice.Title, 16, _chrome.Text),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1) { MarginStart = Dp(14) });
        row.AddView(WidgetSheet.Text(this, choice.CountText, 13, _chrome.Muted));

        row.ContentDescription = $"{choice.Title}, {choice.CountText}";
        row.Click += (_, _) => Choose(choice.BoardId);
        return row;
    }

    void Choose(string boardId)
    {
        WidgetPrefs.SetBoardId(this, _widgetId, boardId);
        WidgetPrefs.SetUndo(this, _widgetId, null);
        ListWidgetProvider.Refresh(ApplicationContext!, _widgetId);
        SetResult(Result.Ok, new Intent().PutExtra(AppWidgetManager.ExtraAppwidgetId, _widgetId));
        Finish();
    }

    int Dp(float dp) => WidgetSheet.Dp(this, dp);
}
