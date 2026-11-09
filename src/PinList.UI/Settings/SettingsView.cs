using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace PinList;

/// <summary>
/// The Settings page, the same on the desktop (a section of the PinList window) and the phone (a tab). What each
/// platform has comes from an <see cref="ISettingsHost"/>. Put the class "phone" on it for touch sizes.
/// Each group is a rounded box of rows with a line between them. On the desktop a row has its buttons on the right,
/// on the phone an action is a whole row you tap (with a chevron).
/// </summary>
public class SettingsView : UserControl
{
    readonly ISettingsHost _host;
    readonly StackPanel Page = new() { Spacing = 16, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Border _frame;

    const string Intro = "Keep your lists in sync across your devices through your own Google Drive.";

    public SettingsView(ISettingsHost host)
    {
        _host = host;
        Background = Brushes.Transparent;
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _frame = new Border { Padding = new Thickness(24, 8, 24, 24), Child = Page },
        };
        Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _host.Changed -= Refresh;
        _host.Changed += Refresh;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _host.Changed -= Refresh;
    }

    bool Phone => Classes.Contains("phone");

    double BodySize => Resource<double>(Phone ? "T.PhoneBody" : "T.Body");

    double SmallSize => Phone ? 13 : 12;

    static T Resource<T>(string key) => (T)Application.Current!.FindResource(key)!;

    /// <summary>Builds the page again from the host (after something changed).</summary>
    public void Refresh()
    {
        _frame.Padding = Phone ? new Thickness(16, 8, 16, 24) : new Thickness(24, 8, 24, 24);
        Page.Children.Clear();
        Page.Children.Add(new TextBlock
        {
            Text = "Settings",
            FontSize = Resource<double>("T.ScreenTitle"),
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(4, 12, 0, 6),
        });
        Page.Children.Add(Appearance());
        if (Sync() is { } sync) Page.Children.Add(sync);
        Page.Children.Add(General(out bool hasUpdateRow));
        Page.Children.Add(YourData());
        Page.Children.Add(Help());
        if (Footer(hasUpdateRow) is { } footer) Page.Children.Add(footer);
    }

    // ================= Groups =================

    /// <summary>A titled box with a line between the rows.</summary>
    Control Group(string title, IEnumerable<Control> rows)
    {
        var inner = new StackPanel();
        foreach (var row in rows)
        {
            if (inner.Children.Count > 0)
            {
                var line = new Border { Height = 1 };
                line.Classes.Add("divider");
                inner.Children.Add(line);
            }
            inner.Children.Add(row);
        }
        var box = new Border { Child = inner };
        box.Classes.Add("group");
        var heading = new TextBlock { Text = title, FontSize = SmallSize };
        heading.Classes.Add("grouptitle");
        return new StackPanel { Children = { heading, box } };
    }

    Control Group(string title, params Control[] rows) => Group(title, (IEnumerable<Control>)rows);

    Control Appearance()
    {
        var grid = new UniformGrid { Columns = 3 };
        foreach (var (key, name) in Themes.AppThemes)
        {
            var button = new ToggleButton
            {
                Content = key == "System" ? "System" : name,
                IsChecked = _host.Theme == key,
                FontSize = Phone ? 14 : 12.5,
            };
            button.Classes.Add("seg");
            AutomationProperties.SetName(button, name);
            string themeKey = key;
            button.Click += (_, _) =>
            {
                _host.SetTheme(themeKey);
                Refresh();
            };
            grid.Children.Add(button);
        }
        var segments = new Border { Child = grid };
        segments.Classes.Add("segments");
        AutomationProperties.SetName(segments, "Theme");

        // the phone has the three choices across the whole row, the desktop has them on the right of "Theme"
        if (Phone) return Group("Appearance", Cell(segments));
        segments.MinWidth = 240;
        return Group("Appearance", Row(Label("Theme"), segments));
    }

    Control? Sync()
    {
        if (_host.Sync is not { } sync)
        {
            if (_host.UnavailableSyncText is not { } text) return null;
            return Group("Google Drive sync", Cell(Muted(text)));
        }

        var rows = new List<Control>();
        if (sync.Account is not { } account)
        {
            // signed out: what sync is, and the Sign in button
            if (Phone)
            {
                rows.Add(Cell(Muted(Intro)));
                rows.AddRange(sync.Actions.Select(a => TapRow(a.Text, a.Run, a.Danger, accent: true, a.Enabled)));
            }
            else
            {
                var buttons = sync.Actions.Select(a => SmallButton(a.Text, a.Run, a.Danger, accent: true, a.Enabled));
                rows.Add(Row(Muted(Intro), Buttons(buttons)));
            }
            return Group("Google Drive sync", rows);
        }

        var who = AccountInfo(account, sync);
        if (Phone)
        {
            rows.Add(Cell(who));
            rows.AddRange(sync.Actions.Select(a => TapRow(a.Text, a.Run, a.Danger, accent: false, a.Enabled)));
        }
        else
        {
            // the everyday buttons next to the account, the dangerous one on a row of its own
            var everyday = sync.Actions.Where(a => !a.Danger).Select(a => SmallButton(a.Text, a.Run, false, false, a.Enabled));
            rows.Add(Row(who, Buttons(everyday)));
            foreach (var danger in sync.Actions.Where(a => a.Danger))
                rows.Add(Row(Muted("Removes your lists from Google Drive and signs you out on this device."),
                    Buttons(new[] { SmallButton(danger.Text, danger.Run, true, false, danger.Enabled) })));
        }
        return Group("Google Drive sync", rows);
    }

    /// <summary>A round badge with the first letter of the account, the address, and a dot with the sync status.</summary>
    Control AccountInfo(string account, SyncSummary sync)
    {
        double size = Phone ? 34 : 30;
        var badge = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#5AA9FF"), 0), new GradientStop(Color.Parse("#A07BFF"), 1) },
            },
            Child = new TextBlock
            {
                Text = account.Length > 0 ? account[..1].ToUpperInvariant() : "?",
                FontWeight = FontWeight.Bold,
                FontSize = Phone ? 14 : 13,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        badge.Classes.Add("avatar");

        var lines = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Label(account, semibold: true));
        if (sync.Problem is { } problem)
        {
            var text = new TextBlock { Text = problem, FontSize = SmallSize };
            text.Classes.Add("problem");
            lines.Children.Add(text);
        }
        else
        {
            var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.Classes.Add(sync.Status.StartsWith("Synced") ? "synced" : sync.Status == "Syncing" ? "syncing" : "notsynced");
            var status = new StackPanel { Orientation = Orientation.Horizontal };
            status.Children.Add(dot);
            status.Children.Add(Muted(sync.Status));
            lines.Children.Add(status);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(lines, 1);
        grid.Children.Add(badge);
        grid.Children.Add(lines);
        return grid;
    }

    Control General(out bool hasUpdateRow)
    {
        var rows = new List<Control>();
        if (_host.AutostartText is { } autostart)
        {
            var row = SwitchRow(autostart, null, _host.AutostartOn, _host.SetAutostart);
            row.IsEnabled = _host.CanChangeAutostart;
            rows.Add(row);
        }
        if (_host.ReminderProblem is { } problem)
        {
            var text = new TextBlock { Text = problem, FontSize = SmallSize, TextWrapping = TextWrapping.Wrap };
            text.Classes.Add("problem");
            var open = SmallButton("Open settings", _host.OpenNotificationSettings);
            AutomationProperties.SetName(open, "Open notification settings");
            rows.Add(Row(new StackPanel { Spacing = 2, Children = { Label("Reminders"), text } }, open));
        }
        hasUpdateRow = _host.UpdatesOn != null;
        if (_host.UpdatesOn is { } updates)
        {
            var get = _host.GetUpdate is { } run ? SmallButton("Get it", run, accent: true) : null;
            if (get != null) AutomationProperties.SetName(get, "Get the new version");
            rows.Add(SwitchRow("Check for updates", _host.VersionText, updates, _host.SetUpdates, get));
        }
        if (rows.Count == 0) rows.Add(Cell(Muted("Nothing to set here on this device.")));
        return Group("General", rows);
    }

    Control YourData()
    {
        var rows = new List<Control>();
        if (Phone)
        {
            rows.Add(TapRow("Export", () => _ = _host.Export()));
            rows.Add(TapRow("Import", () => _ = _host.Import()));
            if (_host.HasDataFolder) rows.Add(TapRow("Open data folder", _host.OpenDataFolder));
        }
        else
        {
            rows.Add(Row(Label("Back up or move your lists"), Buttons(new[]
            {
                SmallButton("Export", () => _ = _host.Export()),
                SmallButton("Import", () => _ = _host.Import()),
            })));
            if (_host.HasDataFolder)
            {
                var open = SmallButton("Open", _host.OpenDataFolder);
                AutomationProperties.SetName(open, "Open data folder");
                rows.Add(Row(Label("Data folder"), Buttons(new[] { open })));
            }
        }
        return Group("Your data", rows);
    }

    Control Help() =>
        Group("Help", Phone
            ? TapRow("Send feedback", _host.SendFeedback)
            : Row(Label("Found a bug or have an idea?"), Buttons(new[] { SmallButton("Send feedback", _host.SendFeedback) })));

    Control? Footer(bool hasUpdateRow)
    {
        var footer = new StackPanel { Spacing = 10, Margin = new Thickness(4, 0, 0, 0) };
        if (!hasUpdateRow) footer.Children.Add(Muted(_host.VersionText));
        if (_host.ExitText is { } exit)
        {
            var button = new Button { Content = exit, FontSize = BodySize, HorizontalAlignment = HorizontalAlignment.Left };
            button.Classes.Add("dialog");
            button.Click += (_, _) => _host.Exit();
            footer.Children.Add(button);
        }
        return footer.Children.Count > 0 ? footer : null;
    }

    // ================= Rows =================

    TextBlock Label(string text, bool semibold = false) => new()
    {
        Text = text,
        FontSize = BodySize,
        FontWeight = semibold ? FontWeight.SemiBold : FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };

    TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, FontSize = SmallSize, VerticalAlignment = VerticalAlignment.Center };
        block.Classes.Add("muted");
        return block;
    }

    /// <summary>One row of a group, with the row padding.</summary>
    Border Cell(Control content)
    {
        var cell = new Border
        {
            Padding = Phone ? new Thickness(16, 13) : new Thickness(14, 11),
            MinHeight = Phone ? 52 : 44,
            Child = content,
        };
        content.VerticalAlignment = VerticalAlignment.Center;
        cell.Classes.Add("setcell");
        return cell;
    }

    /// <summary>Text on the left, a control (or buttons) on the right.</summary>
    Border Row(Control left, Control? right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        left.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(left);
        if (right != null)
        {
            Grid.SetColumn(right, 1);
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(16, 0, 0, 0);
            grid.Children.Add(right);
        }
        return Cell(grid);
    }

    /// <summary>A label (and a line of smaller text under it) with a switch on the right.</summary>
    /// <summary>A row with a switch on its right, and <paramref name="button"/> (if any) before the switch.</summary>
    Border SwitchRow(string label, string? sub, bool isOn, Action<bool> changed, Button? button = null)
    {
        var toggle = new ToggleSwitch { IsChecked = isOn, OnContent = "", OffContent = "", MinWidth = 0 };
        AutomationProperties.SetName(toggle, label);
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        Control left = Label(label);
        if (sub != null)
            left = new StackPanel { Spacing = 2, Children = { Label(label), Muted(sub) } };
        if (button == null) return Row(left, toggle);
        button.VerticalAlignment = VerticalAlignment.Center;
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { button, toggle } };
        return Row(left, right);
    }

    static StackPanel Buttons(IEnumerable<Button> buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.AddRange(buttons);
        return panel;
    }

    /// <summary>A small button for the right side of a desktop row.</summary>
    Button SmallButton(string text, Action run, bool danger = false, bool accent = false, bool enabled = true)
    {
        var button = new Button { Content = text, IsEnabled = enabled };
        button.Classes.Add("dialog");
        button.Classes.Add("small");
        if (danger) button.Classes.Add("danger");
        else if (accent) button.Classes.Add("accent");
        button.Click += (_, _) => run();
        return button;
    }

    /// <summary>A whole row you tap on the phone: the text, and a chevron (none for a dangerous action, which is red).</summary>
    Button TapRow(string text, Action run, bool danger = false, bool accent = false, bool enabled = true)
    {
        var button = new Button { Content = text, FontSize = BodySize, IsEnabled = enabled };
        button.Classes.Add("setrow");
        if (danger) button.Classes.Add("danger");
        else if (accent) button.Classes.Add("accent");
        button.Click += (_, _) => run();
        return button;
    }
}
