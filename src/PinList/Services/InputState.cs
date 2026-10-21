using System;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Input;

namespace PinList;

/// <summary>
/// The few pieces of input state the app reads directly from the hardware: held modifier keys, the mouse
/// button and position, and the system drag-and-drop loop. They go through here so tests can simulate them
/// (a test can't hold down Ctrl or move the real mouse).
/// </summary>
public static class InputState
{
    /// <summary>Currently held Ctrl / Shift / Alt.</summary>
    public static Func<ModifierKeys> Modifiers { get; set; } = () => Keyboard.Modifiers;

    /// <summary>Is the left mouse button held during this mouse event?</summary>
    public static Func<MouseEventArgs, bool> IsLeftButtonPressed { get; set; } = e => e.LeftButton == MouseButtonState.Pressed;

    /// <summary>Mouse position of this event relative to an element.</summary>
    public static Func<MouseEventArgs, IInputElement, Point> Position { get; set; } = (e, relativeTo) => e.GetPosition(relativeTo);

    /// <summary>Mouse cursor on screen, in physical pixels.</summary>
    public static Func<Point> CursorScreen { get; set; } = Native.CursorScreenPosition;

    /// <summary>Runs the system drag-and-drop loop (blocks until the drop).</summary>
    public static Func<DependencyObject, object, DragDropEffects, DragDropEffects> DoDragDrop { get; set; } = SystemDragLoop;

    [ExcludeFromCodeCoverage(Justification = "The Windows drag loop runs until the real mouse button is released.")]
    static DragDropEffects SystemDragLoop(DependencyObject source, object data, DragDropEffects allowed) =>
        DragDrop.DoDragDrop(source, data, allowed);

    public static bool ShiftHeld => (Modifiers() & ModifierKeys.Shift) != 0;

    /// <summary>Puts the real implementations back (after a test).</summary>
    public static void Reset()
    {
        Modifiers = () => Keyboard.Modifiers;
        IsLeftButtonPressed = e => e.LeftButton == MouseButtonState.Pressed;
        Position = (e, relativeTo) => e.GetPosition(relativeTo);
        CursorScreen = Native.CursorScreenPosition;
        DoDragDrop = SystemDragLoop;
    }
}
