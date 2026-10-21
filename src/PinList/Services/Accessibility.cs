using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PinList;

/// <summary>Screen reader announcements and the Windows text size setting.</summary>
public static class A11y
{
    /// <summary>
    /// Says something through the screen reader (Narrator, NVDA, JAWS) without moving focus,
    /// e.g. "Completed: Send the invoice". Does nothing when no screen reader is listening.
    /// </summary>
    public static void Announce(UIElement? source, string text)
    {
        if (source == null || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var peer = UIElementAutomationPeer.FromElement(source) ?? UIElementAutomationPeer.CreatePeerForElement(source);
            peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.ImportantMostRecent, text, "PinList");
        }
        catch { } // announcements are a nice-to-have; never let them break an action
    }

    /// <summary>
    /// Windows "Text size" (Settings → Accessibility → Text size), 1.0 to 2.25.
    /// WPF doesn't apply it on its own, so every window scales its content with it.
    /// </summary>
    public static double TextScale { get; private set; } = WindowsSettings.TextScale();

    /// <summary>Where the text size comes from. Tests replace it instead of changing Windows settings.</summary>
    internal static Func<double> TextScaleSource { get; set; } = WindowsSettings.TextScale;

    /// <summary>Re-reads the text size. Returns true if it changed.</summary>
    public static bool RefreshTextScale()
    {
        double old = TextScale;
        TextScale = TextScaleSource();
        return Math.Abs(old - TextScale) > 0.001;
    }

    /// <summary>Scales the window's content by the Windows text size.</summary>
    public static void ApplyTextScale(Window window)
    {
        if (window.Content is not FrameworkElement root) return;
        root.LayoutTransform = TextScale == 1.0 ? Transform.Identity : new ScaleTransform(TextScale, TextScale);
    }
}

/// <summary>
/// A focusable row (item or category header) that screen readers can see. A plain Border is invisible to
/// UI Automation, so focusing it would announce nothing. The name comes from AutomationProperties.Name.
/// For categories (Tag is a Category) it also supports expand/collapse.
/// </summary>
public class AccessibleRow : Border
{
    public AutomationControlType ControlType { get; set; } = AutomationControlType.ListItem;

    protected override AutomationPeer OnCreateAutomationPeer() => new RowPeer(this);

    sealed class RowPeer : FrameworkElementAutomationPeer, IExpandCollapseProvider
    {
        public RowPeer(AccessibleRow owner) : base(owner) { }

        AccessibleRow Row => (AccessibleRow)Owner;

        protected override AutomationControlType GetAutomationControlTypeCore() => Row.ControlType;
        protected override string GetClassNameCore() => "PinListRow";
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
        protected override bool IsKeyboardFocusableCore() => Owner.Focusable;

        public override object? GetPattern(PatternInterface pattern) =>
            pattern == PatternInterface.ExpandCollapse && Row.Tag is Category ? this : base.GetPattern(pattern);

        public ExpandCollapseState ExpandCollapseState =>
            Row.Tag is Category { Collapsed: true } ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded;

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
