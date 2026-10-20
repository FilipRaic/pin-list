using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;

namespace PinList;

/// <summary>The ⋯ menu and the right-click menus of categories and items.</summary>
public partial class BoardWindow
{
    static MenuItem Item(string header, Action action, string? gesture = null) =>
        App.CreateMenuItem(header, action, gesture);

    /// <summary>Menus use the app theme (colors come from the application resources).</summary>
    static ContextMenu NewMenu(UIElement target, PlacementMode placement) =>
        new() { PlacementTarget = target, Placement = placement };

    void Menu_Click(object sender, RoutedEventArgs e)
    {
        var menu = NewMenu(MenuBtn, PlacementMode.Bottom);
        var colors = new MenuItem { Header = "Window color" };
        foreach (var style in Themes.BoardStyles)
        {
            var mi = Item(style.Name, () => SetStyle(style.Key));
            mi.Icon = Swatch(style.Background, true);
            mi.IsChecked = style.Key == CurrentStyle.Key;
            colors.Items.Add(mi);
        }
        menu.Items.Add(colors);

        var transparent = Item("Transparent background", ToggleTransparency);
        transparent.IsChecked = Board.Transparent;
        menu.Items.Add(transparent);
        menu.Items.Add(App.Instance.ThemeMenu());
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("New window", () => Actions.NewBoard(CurrentWorkArea(), Board)));
        menu.Items.Add(Item("Archive", App.Instance.ShowArchive));
        menu.Items.Add(Item("Export…", () => App.Instance.ExportData(this)));
        menu.Items.Add(Item("Import…", () => App.Instance.ImportData(this)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Delete window", DeleteThisBoard));
        menu.IsOpen = true;
    }

    static Ellipse Swatch(string hex, bool outlined) => new()
    {
        Width = 12,
        Height = 12,
        Fill = Themes.Brush(hex),
        Stroke = outlined ? Themes.Brush("#40808080") : null,
        StrokeThickness = outlined ? 1 : 0,
    };

    static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("IconFont"),
        FontSize = 13,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    void Cat_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not Category cat) return;
        ShowCategoryMenu((FrameworkElement)sender, cat, PlacementMode.MousePoint);
        e.Handled = true;
    }

    /// <summary>Category menu; from the keyboard (context menu key, Shift+F10) it opens below the header.</summary>
    void ShowCategoryMenu(FrameworkElement target, Category cat, PlacementMode placement)
    {
        SetActive(cat);
        var menu = NewMenu(target, placement);
        menu.Items.Add(Item("Rename", () => cat.IsEditing = true, "Double-click"));

        var colors = new MenuItem { Header = "Color" };
        for (int i = 0; i < Actions.Palette.Length; i++)
        {
            string hex = Actions.Palette[i];
            var mi = Item(Actions.PaletteNames[i], () => Actions.SetCategoryColor(cat, hex));
            mi.Icon = Swatch(hex, false);
            mi.IsChecked = string.Equals(cat.Color, hex, StringComparison.OrdinalIgnoreCase);
            colors.Items.Add(mi);
        }
        menu.Items.Add(colors);
        menu.Items.Add(Item(cat.Collapsed ? "Expand" : "Collapse", () => ToggleCollapsed(cat), "Enter"));
        menu.Items.Add(Item("Move up", () => MoveCategoryBy(cat, -1), "Alt+Up"));
        menu.Items.Add(Item("Move down", () => MoveCategoryBy(cat, +1), "Alt+Down"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Delete category", () => DeleteWithFocus(cat), "Del"));
        menu.IsOpen = true;
    }

    void Item_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not TodoItem item) return;
        ShowItemMenu((FrameworkElement)sender, item, PlacementMode.MousePoint);
        e.Handled = true;
    }

    /// <summary>Item menu; from the keyboard (context menu key, Shift+F10) it opens below the row.</summary>
    void ShowItemMenu(FrameworkElement target, TodoItem item, PlacementMode placement)
    {
        SetActive(item);
        var menu = NewMenu(target, placement);
        menu.Items.Add(Item("Edit", () => item.IsEditing = true, "Enter"));
        menu.Items.Add(Item("Complete", () => CompleteNow(item), "Space"));
        menu.Items.Add(Item("Move up", () => MoveItemBy(item, -1), "Alt+Up"));
        menu.Items.Add(Item("Move down", () => MoveItemBy(item, +1), "Alt+Down"));
        menu.Items.Add(new Separator());

        var due = Item(item.HasDue ? "Change due date…" : "Add due date…", () => EditDueDate(item));
        due.Icon = Glyph(""); // calendar
        menu.Items.Add(due);
        if (item.HasDue)
            menu.Items.Add(Item("Remove due date", () => { Actions.SetDueDate(item, null); A11y.Announce(this, "Due date removed"); FocusRow(item); }));

        var reminder = Item(item.HasPendingReminder ? "Change reminder…" : "Add reminder…", () => EditReminder(item));
        reminder.Icon = Glyph(""); // bell
        menu.Items.Add(reminder);
        if (item.ReminderAt != null)
            menu.Items.Add(Item("Remove reminder", () => { Actions.SetReminder(item, null); A11y.Announce(this, "Reminder removed"); FocusRow(item); }));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Delete", () => DeleteWithFocus(item), "Del"));
        menu.IsOpen = true;
    }

    void EditDueDate(TodoItem item)
    {
        var picked = Dialogs.Current.PickDueDate(this, item.DueDate);
        if (picked.HasValue)
        {
            Actions.SetDueDate(item, picked.Value);
            A11y.Announce(this, $"Due {item.DueText}");
        }
        FocusRow(item);
    }

    void EditReminder(TodoItem item)
    {
        var current = item.HasPendingReminder ? item.ReminderAt : null;
        var picked = Dialogs.Current.PickReminder(this, current, item.DueDate);
        if (picked.HasValue)
        {
            Actions.SetReminder(item, picked.Value);
            A11y.Announce(this, $"Reminder set for {item.ReminderText}");
        }
        FocusRow(item);
    }
}
