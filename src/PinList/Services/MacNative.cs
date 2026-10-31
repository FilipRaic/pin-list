#if !WINDOWS
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace PinList;

/// <summary>
/// macOS: makes a widget window behave like a desktop widget. It shows on every Space, stays where it is when
/// "Show Desktop" or Mission Control moves the other windows aside, and isn't part of Cmd+` window cycling.
/// (The Dock icon and Cmd+Tab entry are left out by LSUIElement in the app's Info.plist.)
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Objective-C calls on a real NSWindow; only runs on a Mac.")]
static class MacNative
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nuint SendGetULong(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendSetULong(IntPtr receiver, IntPtr selector, nuint value);

    // NSWindowCollectionBehavior
    const nuint CanJoinAllSpaces = 1 << 0;
    const nuint Stationary = 1 << 4;
    const nuint IgnoresCycle = 1 << 6;

    public static void PinToDesktop(Window window)
    {
        if (!OperatingSystem.IsMacOS() || window.TryGetPlatformHandle() is not { HandleDescriptor: "NSWindow" } h || h.Handle == IntPtr.Zero)
            return;
        try
        {
            var behavior = SendGetULong(h.Handle, sel_registerName("collectionBehavior"));
            SendSetULong(h.Handle, sel_registerName("setCollectionBehavior:"), behavior | CanJoinAllSpaces | Stationary | IgnoresCycle);
        }
        catch (Exception ex) { Store.Log("Desktop widget behavior: " + ex.Message); }
    }
}
#endif
