using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

namespace PinList.Android;

/// <summary>Gives the launcher the rows of a home screen widget's list (category headers and items).</summary>
[Service(Permission = "android.permission.BIND_REMOTEVIEWS", Exported = false)]
[ExcludeFromCodeCoverage(Justification = "Runs inside Android's launcher widgets.")]
public class ListWidgetService : RemoteViewsService
{
    public override IRemoteViewsFactory OnGetViewFactory(Intent? intent) =>
        new Rows(ApplicationContext!, intent?.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId)
                                      ?? AppWidgetManager.InvalidAppwidgetId);

    [ExcludeFromCodeCoverage(Justification = "Runs inside Android's launcher widgets.")]
    sealed class Rows : Java.Lang.Object, IRemoteViewsFactory
    {
        readonly Context _context;
        readonly int _widgetId;
        IReadOnlyList<WidgetRow> _rows = Array.Empty<WidgetRow>();
        WidgetLook? _look;

        public Rows(Context context, int widgetId)
        {
            _context = context;
            _widgetId = widgetId;
        }

        public void OnCreate() { }

        public void OnDestroy() { }

        /// <summary>The launcher asks for the rows again (after ListWidgetProvider refreshed the widget).</summary>
        public void OnDataSetChanged()
        {
            try
            {
                var state = WidgetData.State(_context, WidgetPrefs.BoardId(_context, _widgetId));
                _rows = state.Rows;
                _look = state.Look;
            }
            catch (Exception ex) { Store.Log("Widget rows: " + ex.Message); } // keeps the rows it had
        }

        public int Count => _rows.Count;

        public long GetItemId(int position) => position;

        public bool HasStableIds => false;

        public int ViewTypeCount => 2;

        public RemoteViews? LoadingView => null;

        public RemoteViews? GetViewAt(int position)
        {
            if (position < 0 || position >= _rows.Count || _look is not { } look) return null;
            var row = _rows[position];
            return row.Kind == WidgetRowKind.Category ? Category(row) : Item(row, look);
        }

        RemoteViews Category(WidgetRow row)
        {
            var views = new RemoteViews(_context.PackageName, Resource.Layout.list_widget_category);
            views.SetImageViewResource(Resource.Id.category_arrow,
                row.Collapsed ? Resource.Drawable.widget_collapsed : Resource.Drawable.widget_expanded);
            ListWidgetProvider.Tint(views, Resource.Id.category_arrow, row.Color);
            views.SetTextViewText(Resource.Id.category_name, row.Text);
            views.SetTextColor(Resource.Id.category_name, ListWidgetProvider.Color(row.Color));
            views.SetTextViewText(Resource.Id.category_count, row.Count.ToString());
            views.SetTextColor(Resource.Id.category_count, ListWidgetProvider.Color(row.Color));
            views.SetContentDescription(Resource.Id.category_row, row.AccessibleName);
            views.SetOnClickFillInIntent(Resource.Id.category_row, Tapped(WidgetTapActivity.Collapse, row.Id));
            return views;
        }

        RemoteViews Item(WidgetRow row, WidgetLook look)
        {
            var views = new RemoteViews(_context.PackageName, Resource.Layout.list_widget_item);
            ListWidgetProvider.Tint(views, Resource.Id.item_check, row.Color);
            views.SetContentDescription(Resource.Id.item_check, WidgetModel.CompleteName(row.Text));
            views.SetOnClickFillInIntent(Resource.Id.item_check, Tapped(WidgetTapActivity.Complete, row.Id));

            views.SetTextViewText(Resource.Id.item_text, row.Text);
            views.SetTextColor(Resource.Id.item_text, ListWidgetProvider.Color(look.Text));
            views.SetViewVisibility(Resource.Id.item_due, ListWidgetProvider.Shown(row.Due.Length > 0));
            views.SetTextViewText(Resource.Id.item_due, row.Due);
            views.SetTextColor(Resource.Id.item_due, ListWidgetProvider.Color(row.DueColor));
            views.SetContentDescription(Resource.Id.item_body, row.AccessibleName);
            views.SetOnClickFillInIntent(Resource.Id.item_body, Tapped(WidgetTapActivity.Open, row.Id));
            return views;
        }

        /// <summary>What a tap on a row adds to the widget's row template (see ListWidgetProvider.RowTemplate).</summary>
        static Intent Tapped(string action, string id) =>
            new Intent().PutExtra(WidgetTapActivity.ActionExtra, action)!.PutExtra(WidgetTapActivity.IdExtra, id)!;
    }
}
