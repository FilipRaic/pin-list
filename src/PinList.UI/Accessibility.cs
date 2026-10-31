using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace PinList;

/// <summary>How PinList's windows draw text.</summary>
public static class Look
{
    /// <summary>
    /// Grayscale text smoothing: colored (subpixel) smoothing shows color fringes on semi-transparent widgets.
    /// </summary>
    public static void GrayscaleText(Visual window) =>
        Avalonia.Media.TextOptions.SetTextRenderingMode(window, Avalonia.Media.TextRenderingMode.Antialias);
}

/// <summary>Screen reader announcements and the operating system's text size.</summary>
public static class A11y
{
    /// <summary>
    /// Says something through the screen reader without moving focus, e.g. "Completed Send the invoice".
    /// Each window has a live region (<see cref="LiveRegion"/>); changing its text makes screen readers read it.
    /// Does nothing when the window has none.
    /// </summary>
    public static void Announce(Visual? source, string text)
    {
        if (source == null || string.IsNullOrWhiteSpace(text)) return;
        if (TopLevel.GetTopLevel(source) is not { } top || top.FindDescendantOfType<LiveRegion>() is not { } region) return;
        LastAnnouncement = text;
        region.Text = ""; // a repeated message must still count as a change
        region.Text = text;
    }

    /// <summary>The last thing announced (tests check it).</summary>
    public static string? LastAnnouncement { get; internal set; }

    /// <summary>Text size factor of the operating system (Windows: Settings → Accessibility → Text size), 1.0 to 2.25.</summary>
    public static double TextScale { get; private set; } = 1.0;

    /// <summary>Where the text size comes from. Each platform sets it; tests replace it.</summary>
    public static Func<double> TextScaleSource { get; set; } = () => 1.0;

    /// <summary>Re-reads the text size. Returns true if it changed.</summary>
    public static bool RefreshTextScale()
    {
        double old = TextScale;
        TextScale = TextScaleSource();
        return Math.Abs(old - TextScale) > 0.001;
    }
}

/// <summary>An invisible text that screen readers read whenever it changes (see <see cref="A11y.Announce"/>).</summary>
public class LiveRegion : TextBlock
{
    public LiveRegion()
    {
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Assertive);
        Opacity = 0;
        IsHitTestVisible = false;
        Width = 1;
        Height = 1;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);
}

/// <summary>
/// A focusable row (item or category header) that screen readers can see. The name comes from
/// AutomationProperties.Name. For categories (Tag is a Category) it also supports expand/collapse.
/// </summary>
public class AccessibleRow : Border
{
    public AccessibleRow() => Focusable = true;

    public AutomationControlType ControlType { get; set; } = AutomationControlType.ListItem;

    /// <summary>Categories expand and collapse; items don't (a screen reader would announce them as expandable).</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => Tag is Category ? new CategoryPeer(this) : new RowPeer(this);

    class RowPeer : ControlAutomationPeer
    {
        public RowPeer(AccessibleRow owner) : base(owner) { }

        protected AccessibleRow Row => (AccessibleRow)Owner;

        protected override AutomationControlType GetAutomationControlTypeCore() => Row.ControlType;
        protected override string GetClassNameCore() => "PinListRow";
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
    }

    sealed class CategoryPeer : RowPeer, IExpandCollapseProvider
    {
        public CategoryPeer(AccessibleRow owner) : base(owner) { }

        public ExpandCollapseState ExpandCollapseState =>
            Row.Tag is Category { Collapsed: true } ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded;

        public bool ShowsMenu => false;

        public void Expand() => SetCollapsed(false);
        public void Collapse() => SetCollapsed(true);

        void SetCollapsed(bool collapsed)
        {
            if (Row.Tag is not Category c || c.Collapsed == collapsed) return;
            c.Collapsed = collapsed;
            Store.MarkDirty();
        }
    }
}
