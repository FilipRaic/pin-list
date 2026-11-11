using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PinList.Sync;

namespace PinList;

/// <summary>The ⋯ menu and the right-click menus of categories and items. No menu has a submenu.</summary>
public partial class BoardWindow
{
    static MenuItem Item(string header, Action action, string? gesture = null, string? icon = null, bool danger = false) =>
        App.CreateMenuItem(header, action, gesture, icon, danger);

    /// <summary>The last menu opened (tests look at it).</summary>
    internal ContextMenu? LastMenu { get; private set; }

    /// <summary>Menus use the app theme (colors come from the application resources).</summary>
    ContextMenu NewMenu(Control target, PlacementMode placement)
    {
        var menu = new ContextMenu { Placement = placement, PlacementTarget = target };
        LastMenu = menu;
        return menu;
    }

    static void Open(ContextMenu menu, Control target) => menu.Open(target);

    void Menu_Click(object? sender, RoutedEventArgs e)
    {
        var menu = NewMenu(MenuBtn, PlacementMode.Bottom);

        // list color: one row of swatches, the current one ringed
        var styles = Themes.BoardStyles.Select(s => (s.Name, s.Background)).ToList();
        menu.Items.Add(new SwatchMenuItem("List color", styles, CurrentStyle.Background,
            hex => SetStyle(Themes.BoardStyles.First(s => s.Background == hex).Key)));
        menu.Items.Add(App.CreateSwitch("Transparent", Board.Transparent, ToggleTransparency, "Line.Transparent"));
        if (SyncService.Current?.IsAvailable == true)
            menu.Items.Add(App.CreateSwitch("Sync this list", Board.Synced, () => _ = App.Instance.SetBoardSynced(Board, !Board.Synced, this), "Line.Sync"));
        menu.Items.Add(new Separator());

        var copyAll = Item("Copy all items", () => Copying.Board(this, Board), icon: "Line.Copy");
        copyAll.IsEnabled = CopyText.Count(Board) > 0;
        menu.Items.Add(copyAll);
        menu.Items.Add(Item("New list", () => Actions.NewBoard(CurrentWorkArea().ToWorkArea(), Board), icon: "Line.Add"));
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("All lists", App.Instance.ShowListsWindow, icon: "Line.Grid"));
        menu.Items.Add(Item("Archive", App.Instance.ShowArchive, icon: "Line.Archive"));
        menu.Items.Add(Item("Settings", App.Instance.ShowSettings, icon: "Line.Settings"));
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("Hide widget", RemoveFromDesktop, icon: "Line.RemoveFromDesktop"));
        menu.Items.Add(Item("Delete list", DeleteThisBoard, icon: "Line.Delete", danger: true));
        Open(menu, MenuBtn);
    }

    void Cat_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (((Control)sender!).Tag is not Category cat) return;
        // the context menu key and Shift+F10 are handled in Cat_KeyDown (the menu opens below the row there)
        ShowCategoryMenu((Control)sender, cat, PlacementMode.Pointer);
        e.Handled = true;
    }

    /// <summary>Category menu; from the keyboard (context menu key, Shift+F10) it opens below the header.</summary>
    void ShowCategoryMenu(Control target, Category cat, PlacementMode placement)
    {
        SetActive(cat);
        var menu = NewMenu(target, placement);
        menu.Items.Add(Item("Rename", () => cat.IsEditing = true, "Double-click", "Line.Rename"));

        var palette = Actions.Palette.Select((hex, i) => (Actions.PaletteNames[i], hex)).ToList();
        menu.Items.Add(new SwatchMenuItem("Color", palette, cat.Color, hex => Actions.SetCategoryColor(cat, hex)));
        menu.Items.Add(Item(cat.Collapsed ? "Expand" : "Collapse", () => ToggleCollapsed(cat), OsText.Enter,
            cat.Collapsed ? "Line.ChevronDown" : "Line.ChevronUp"));
        menu.Items.Add(Item("Move up", () => MoveCategoryBy(cat, -1), OsText.Alt("Up"), "Line.ArrowUp"));
        menu.Items.Add(Item("Move down", () => MoveCategoryBy(cat, +1), OsText.Alt("Down"), "Line.ArrowDown"));
        var copy = Item("Copy items", () => Copying.Category(this, cat), OsText.CopyGesture, "Line.Copy");
        copy.IsEnabled = cat.Items.Count > 0;
        menu.Items.Add(copy);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Delete category", () => DeleteWithFocus(cat), OsText.Delete, "Line.Delete", danger: true));
        Open(menu, target);
    }

    void Item_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (((Control)sender!).Tag is not TodoItem item) return;
        ShowItemMenu((Control)sender, item, PlacementMode.Pointer);
        e.Handled = true;
    }

    /// <summary>Item menu; from the keyboard (context menu key, Shift+F10) it opens below the row.</summary>
    void ShowItemMenu(Control target, TodoItem item, PlacementMode placement)
    {
        SetActive(item);
        var menu = NewMenu(target, placement);
        menu.Items.Add(Item("Edit", () => item.IsEditing = true, OsText.Enter, "Line.Rename"));
        menu.Items.Add(Item("Complete", () => { if (!item.IsCompleting) ToggleCompleting(item); }, "Space", "Line.Check"));
        menu.Items.Add(Item("Move up", () => MoveItemBy(item, -1), OsText.Alt("Up"), "Line.ArrowUp"));
        menu.Items.Add(Item("Move down", () => MoveItemBy(item, +1), OsText.Alt("Down"), "Line.ArrowDown"));
        menu.Items.Add(Item("Copy", () => Copying.Item(this, item), OsText.CopyGesture, "Line.Copy"));
        menu.Items.Add(new Separator());

        menu.Items.Add(Item(item.HasDue ? "Change due date" : "Add due date", () => EditDueDate(item), icon: "Line.Calendar"));
        if (item.HasDue)
            menu.Items.Add(Item("Remove due date", () => { Actions.SetDueDate(item, null); A11y.Announce(this, "Due date removed"); FocusRow(item); }, icon: "Line.Calendar"));

        menu.Items.Add(Item(item.HasPendingReminder ? "Change reminder" : "Add reminder", () => EditReminder(item), icon: "Line.Bell"));
        if (item.HasPendingReminder)
            menu.Items.Add(Item("Remove reminder", () => { Actions.SetReminder(item, null); A11y.Announce(this, "Reminder removed"); FocusRow(item); }, icon: "Line.Bell"));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Delete", () => DeleteWithFocus(item), OsText.Delete, "Line.Delete", danger: true));
        Open(menu, target);
    }

    async void EditDueDate(TodoItem item)
    {
        var picked = await Dialogs.Current.PickDueDate(this, item.DueDate);
        if (picked.HasValue)
        {
            Actions.SetDueDate(item, picked.Value);
            A11y.Announce(this, $"Due {item.DueText}");
        }
        FocusRow(item);
    }

    async void EditReminder(TodoItem item)
    {
        var current = item.HasPendingReminder ? item.ReminderAt : null;
        var picked = await Dialogs.Current.PickReminder(this, current, item.DueDate);
        if (picked.HasValue)
        {
            Actions.SetReminder(item, picked.Value);
            A11y.Announce(this, $"Reminder set for {item.ReminderText}");
        }
        FocusRow(item);
    }
}
