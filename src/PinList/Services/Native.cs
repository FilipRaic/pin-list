using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PinList;

/// <summary>
/// Win32 tricks that make a window behave like a desktop widget:
///  - its owner is "Progman" (the desktop), so Win+D / "Show desktop" doesn't hide it
///  - WS_EX_TOOLWINDOW: not shown in the taskbar or in Alt+Tab
/// </summary>
static class Native
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x00000080;
    const long WS_EX_APPWINDOW = 0x00040000;

    /// <summary>Must be called before Show().</summary>
    public static void PinToDesktop(Window window)
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
            new WindowInteropHelper(window).Owner = progman;
    }

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT point);

    /// <summary>Cursor position on screen in physical pixels (reliable during drag and drop too).</summary>
    public static Point CursorScreenPosition()
    {
        GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_CAPTION_COLOR = 35; // Windows 11 only

    public static void BringToForeground(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
    }

    /// <summary>Title bar matching the window color (dark/light) – used by the archive window.</summary>
    public static void StyleTitleBar(Window window, bool dark, System.Windows.Media.Color caption)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int d = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int));
            int c = caption.R | (caption.G << 8) | (caption.B << 16); // COLORREF = 0x00BBGGRR
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref c, sizeof(int));
        }
        catch { }
    }

    public static void MakeToolWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }
}
