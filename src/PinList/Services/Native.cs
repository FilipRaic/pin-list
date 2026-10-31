using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PinList;

/// <summary>
/// Operating-system tricks that make a window behave like a desktop widget. On Windows:
///  - its owner is "Progman" (the desktop), so Win+D / "Show desktop" doesn't hide it
///  - WS_EX_TOOLWINDOW: not shown in the taskbar or in Alt+Tab
/// Elsewhere these do nothing (macOS has its own version in Platform/Mac).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Win32 calls on real window handles; the headless tests have none. Checked on Windows by hand.")]
static class Native
{
    /// <summary>The window's HWND; zero for windows without one (tests run on Avalonia's headless platform).</summary>
    static IntPtr Handle(TopLevel window) =>
        window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } h ? h.Handle : IntPtr.Zero;

#if WINDOWS
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    const int GWL_EXSTYLE = -20;
    const int GWLP_HWNDPARENT = -8;
    const long WS_EX_TOOLWINDOW = 0x00000080;
    const long WS_EX_APPWINDOW = 0x00040000;

    /// <summary>The desktop window ("Progman") the widgets belong to. Tests point this at a window of their own.</summary>
    internal static Func<IntPtr> FindDesktopWindow { get; set; } = () => FindWindow("Progman", null);

    internal const long ToolWindowStyle = WS_EX_TOOLWINDOW;

    /// <summary>Extended window style (to check the tool-window flag).</summary>
    internal static long GetExStyle(TopLevel window) => GetWindowLongPtr(Handle(window), GWL_EXSTYLE).ToInt64();

    /// <summary>The window's owner (to check it belongs to the desktop).</summary>
    internal static IntPtr GetOwner(TopLevel window) => GetWindowLongPtr(Handle(window), GWLP_HWNDPARENT);

    /// <summary>Makes the desktop the owner of the window, so "Show desktop" leaves it visible.</summary>
    public static void PinToDesktop(Window window)
    {
        var hwnd = Handle(window);
        var progman = FindDesktopWindow();
        if (hwnd != IntPtr.Zero && progman != IntPtr.Zero) SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, progman);
    }

    public static void MakeToolWindow(TopLevel window)
    {
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero) return;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int dwProcessId);

    /// <summary>
    /// Lets the already running PinList bring its widgets to the front. Windows only allows that for a
    /// process the user just interacted with, so the second instance (which the user just started) passes it on.
    /// </summary>
    public static void AllowAnyForeground() => AllowSetForegroundWindow(-1); // ASFW_ANY

    public static void BringToForeground(TopLevel window)
    {
        var hwnd = Handle(window);
        if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);

    /// <summary>Cursor position on screen in physical pixels.</summary>
    [ExcludeFromCodeCoverage(Justification = "Reads the real mouse.")]
    public static PixelPoint CursorScreenPosition()
    {
        GetCursorPos(out var p);
        return new PixelPoint(p.X, p.Y);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_CAPTION_COLOR = 35; // Windows 11 only

    /// <summary>Title bar matching the window color (dark/light) – used by the archive window.</summary>
    public static void StyleTitleBar(TopLevel window, bool dark, Color caption)
    {
        var hwnd = Handle(window);
        if (hwnd == IntPtr.Zero) return;
        // These return an error code instead of throwing (e.g. the caption color on Windows 10); that's fine
        int d = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int));
        int c = caption.R | (caption.G << 8) | (caption.B << 16); // COLORREF = 0x00BBGGRR
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref c, sizeof(int));
    }

    // ---- Contrast themes ----

    [StructLayout(LayoutKind.Sequential)]
    struct HIGHCONTRAST { public int cbSize; public int dwFlags; public IntPtr lpszDefaultScheme; }
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(int uiAction, int uiParam, ref HIGHCONTRAST pvParam, int fWinIni);
    [DllImport("user32.dll")] static extern int GetSysColor(int nIndex);
    const int SPI_GETHIGHCONTRAST = 0x0042;
    const int HCF_HIGHCONTRASTON = 0x1;

    /// <summary>The contrast theme's colors if a Windows contrast theme is on, otherwise null.</summary>
    public static ContrastColors? SystemContrastColors()
    {
        var hc = new HIGHCONTRAST { cbSize = Marshal.SizeOf<HIGHCONTRAST>() };
        if (!SystemParametersInfo(SPI_GETHIGHCONTRAST, hc.cbSize, ref hc, 0) || (hc.dwFlags & HCF_HIGHCONTRASTON) == 0)
            return null;
        static Color Sys(int index)
        {
            int c = GetSysColor(index);
            return Color.FromRgb((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
        }
        // COLOR_WINDOW, COLOR_WINDOWTEXT, COLOR_HIGHLIGHT, COLOR_HIGHLIGHTTEXT, COLOR_HOTLIGHT
        return new ContrastColors(Sys(5), Sys(8), Sys(13), Sys(14), Sys(26));
    }

    [DllImport("user32.dll")] static extern uint GetDpiForSystem();

    /// <summary>Scale of the primary monitor at sign-in; window positions are stored in units of it (like WPF did).</summary>
    public static double SystemScale()
    {
        try { return GetDpiForSystem() / 96.0; }
        catch (EntryPointNotFoundException) { return 1.0; } // before Windows 10 1607
    }
#else
    public static void PinToDesktop(Window window) => MacNative.PinToDesktop(window);
    public static void MakeToolWindow(TopLevel window) { }
    public static void AllowAnyForeground() { }
    public static void BringToForeground(TopLevel window) { }
    public static PixelPoint CursorScreenPosition() => default;
    public static void StyleTitleBar(TopLevel window, bool dark, Color caption) { }
    public static ContrastColors? SystemContrastColors() => null;
    public static double SystemScale() => 1.0;
#endif
}
