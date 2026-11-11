using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Android.App;
using Android.Appwidget;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.AppCompat.App;

namespace PinList.Android;

/// <summary>
/// The box that adds items from a home screen widget (Android doesn't allow typing inside a widget). A small
/// native screen over the home screen, so it opens at once: Enter adds the item and the box stays for the next
/// one, Back closes it. It looks like the app's own sheets (see <see cref="WidgetSheet"/>).
/// </summary>
[Activity(Theme = "@style/PinListDialogTheme", Exported = false, NoHistory = true, ExcludeFromRecents = true,
    TaskAffinity = "", WindowSoftInputMode = SoftInput.StateAlwaysVisible | SoftInput.AdjustResize)]
[ExcludeFromCodeCoverage(Justification = "A native Android screen.")]
public class AddItemActivity : AppCompatActivity
{
    int _widgetId;
    string? _boardId;
    List<WidgetRow> _categories = new();
    readonly List<TextView> _chips = new();
    int _selected;
    WidgetChrome _chrome = null!;
    EditText _text = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _widgetId = Intent?.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId)
                    ?? AppWidgetManager.InvalidAppwidgetId;
        _boardId = WidgetPrefs.BoardId(this, _widgetId);
        var state = WidgetData.State(this, _boardId);
        _categories = state.Rows.Where(r => r.Kind == WidgetRowKind.Category).ToList();
        if (_boardId == null || _categories.Count == 0)
        {
            Close(); // the list was deleted, or lost its last category, since the widget was drawn
            return;
        }

        _chrome = WidgetSheet.Chrome(this);
        var card = WidgetSheet.Card(this, _chrome);
        var title = WidgetSheet.Text(this, WidgetModel.AddTitle(state.Title), 17, _chrome.Text, bold: true);
        title.SetMaxLines(1);
        title.Ellipsize = TextUtils.TruncateAt.End;
        card.AddView(title);

        // The categories as a row of chips (only when there is a choice). It starts on the one used last here.
        _selected = System.Math.Max(0, _categories.FindIndex(c => c.Id == WidgetPrefs.LastCategory(this, _widgetId)));
        if (_categories.Count > 1) card.AddView(Chips(), Below(12));

        _text = new EditText(this)
        {
            Hint = "Add item",
            InputType = InputTypes.ClassText | InputTypes.TextFlagCapSentences,
            ImeOptions = ImeAction.Done,
            Background = WidgetSheet.Rounded(this, _chrome.Field, 12, _chrome.Accent, 1.5f),
        };
        _text.SetSingleLine(true);
        _text.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 16);
        _text.SetTextColor(WidgetSheet.Color(_chrome.Text));
        _text.SetHintTextColor(WidgetSheet.Color(_chrome.Muted));
        _text.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
        _text.ImportantForAutofill = ImportantForAutofill.No;
        _text.EditorAction += (_, e) =>
        {
            // Enter on the keyboard (shown as "done"), or on a real one
            if (e.ActionId == ImeAction.Done || e.Event is { KeyCode: Keycode.Enter, Action: KeyEventActions.Down }) Add();
            e.Handled = true; // the keyboard stays open for the next item
        };
        card.AddView(_text, Below(12));

        WidgetSheet.Show(this, card);
        Window!.SetGravity(GravityFlags.Bottom);
        Window.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        _text.RequestFocus();
    }

    View Chips()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        for (int i = 0; i < _categories.Count; i++)
        {
            int index = i;
            var chip = WidgetSheet.Text(this, _categories[i].Text, 13, _chrome.Text);
            chip.SetPadding(Dp(12), Dp(7), Dp(12), Dp(7));
            chip.SetMaxLines(1);
            chip.Clickable = true;
            chip.Focusable = true;
            chip.Click += (_, _) =>
            {
                _selected = index;
                ColorChips();
            };
            _chips.Add(chip);
            row.AddView(chip, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                MarginEnd = Dp(8),
            });
        }
        ColorChips();

        var scroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        scroll.AddView(row);
        return scroll;
    }

    /// <summary>The chosen category's chip is filled with the accent color, the others are quiet.</summary>
    void ColorChips()
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            bool on = i == _selected;
            _chips[i].Background = WidgetSheet.Rounded(this, on ? _chrome.Accent : _chrome.Field, 16);
            _chips[i].SetTextColor(WidgetSheet.Color(on ? _chrome.AccentText : _chrome.Text));
            _chips[i].Selected = on; // screen readers say which one is chosen
        }
    }

    void Add()
    {
        string text = _text.Text ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;
        string categoryId = _categories[_selected].Id;
        string boardId = _boardId!;
        bool added = WidgetData.Run(this, true, () => WidgetActions.AddItem(boardId, categoryId, text) != null);
        if (!added)
        {
            Close(); // the list is gone
            return;
        }

        _text.Text = "";
        WidgetPrefs.SetLastCategory(this, _widgetId, categoryId);
        WidgetPrefs.SetUndo(this, _widgetId, null);
        WidgetTapActivity.Changed(ApplicationContext!, _widgetId, boardId);
    }

    void Close()
    {
        ListWidgetProvider.Refresh(ApplicationContext!, _widgetId);
        Finish();
    }

    int Dp(float dp) => WidgetSheet.Dp(this, dp);

    LinearLayout.LayoutParams Below(float gapDp) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(gapDp) };
}
