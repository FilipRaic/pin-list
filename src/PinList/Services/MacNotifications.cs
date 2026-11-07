#if !WINDOWS
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace PinList;

/// <summary>
/// macOS reminders as macOS notifications (UserNotifications), so they're in Notification Center. Info.plist asks for
/// the "Alerts" style, which stays on the screen until it's closed (the user can change the style in System
/// Settings → Notifications → PinList). Clicking one runs the reminder's action.
/// Reminders that come before macOS answered the permission question wait for the answer. When macOS says no (or
/// can't be asked, or doesn't take a notification), the reminder shows in PinList's own popup instead, which also
/// stays until it's closed, and log.txt says why.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Objective-C calls into macOS; only runs on a Mac.")]
static class MacNotifications
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    const string UserNotifications = "/System/Library/Frameworks/UserNotifications.framework/UserNotifications";
    const string LibSystem = "/usr/lib/libSystem.B.dylib";

    [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, IntPtr extraBytes);
    [DllImport(ObjC)] static extern void objc_registerClassPair(IntPtr cls);
    [DllImport(ObjC)] [return: MarshalAs(UnmanagedType.I1)] static extern bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);
    [DllImport(ObjC)] static extern IntPtr objc_getProtocol(string name);
    [DllImport(ObjC)] [return: MarshalAs(UnmanagedType.I1)] static extern bool class_addProtocol(IntPtr cls, IntPtr protocol);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, IntPtr c);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern long SendGetLong(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendOptions(IntPtr receiver, IntPtr selector, nuint options, IntPtr block);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendString(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    // UNAuthorizationOptions and UNNotificationPresentationOptions
    const nuint Sound = 1 << 1, Alert = 1 << 2, List = 1 << 3, Banner = 1 << 4;
    const int BlockIsGlobal = 1 << 28;

    // A block is { isa, flags, reserved, invoke, descriptor } (32 bytes); the invoke pointer is at offset 16.
    // The blocks made here are "global" (macOS never copies or frees them); a request's block carries the request's
    // number in 8 more bytes at offset 32.
    delegate void AuthorizedInvoke(IntPtr block, byte granted, IntPtr error);
    delegate void AddedInvoke(IntPtr block, IntPtr error);
    delegate void SettingsInvoke(IntPtr block, IntPtr settings);
    delegate void PresentationInvoke(IntPtr block, nuint options);
    delegate void DoneInvoke(IntPtr block);
    delegate void WillPresent(IntPtr self, IntPtr cmd, IntPtr center, IntPtr notification, IntPtr completion);
    delegate void DidReceive(IntPtr self, IntPtr cmd, IntPtr center, IntPtr response, IntPtr completion);

    // kept alive for as long as macOS may call them
    static readonly AuthorizedInvoke OnAuthorized = Authorized;
    static readonly AddedInvoke OnAdded = Added;
    static readonly SettingsInvoke OnSettings = SettingsRead;
    static IntPtr _settingsBlock;
    static readonly WillPresent OnWillPresent = WillPresentNotification;
    static readonly DidReceive OnDidReceive = DidReceiveResponse;

    enum Permission { NotAsked, Asking, Granted, Denied }

    sealed record Reminder(string Id, string Title, string Text, Action Click);

    static readonly Dictionary<string, Action> Clicks = new();
    static readonly List<Reminder> Waiting = new();
    static readonly Dictionary<long, Reminder> Sending = new();
    static long _nextNumber;
    static IntPtr _center;
    static bool _started;
    static Permission _permission;

    /// <summary>Connects and asks for permission when PinList starts (before the first reminder can come).</summary>
    public static void Prepare()
    {
        if (OperatingSystem.IsMacOS()) Start();
    }

    /// <summary>
    /// Shows a reminder as a macOS notification, or keeps it until macOS answered the permission question. False
    /// when macOS notifications can't be used: the caller shows PinList's popup then.
    /// </summary>
    public static bool TryShow(string title, string text, Action onClick)
    {
        if (!OperatingSystem.IsMacOS() || !Start() || _permission == Permission.Denied) return false;
        var reminder = new Reminder(Guid.NewGuid().ToString("N"), title, text, onClick);
        if (_permission == Permission.Granted) return Add(reminder);
        Waiting.Add(reminder); // shown once macOS answered (Authorized)
        return true;
    }

    /// <summary>Hands the notification to macOS. Its answer comes in Added (an error shows the popup then).</summary>
    static bool Add(Reminder r)
    {
        try
        {
            var content = Send(Send(objc_getClass("UNMutableNotificationContent"), Sel("alloc")), Sel("init"));
            Send(content, Sel("setTitle:"), NSString(r.Title));
            Send(content, Sel("setBody:"), NSString(r.Text));
            Send(content, Sel("setSound:"), Send(objc_getClass("UNNotificationSound"), Sel("defaultSound")));
            var request = Send(objc_getClass("UNNotificationRequest"), Sel("requestWithIdentifier:content:trigger:"),
                NSString(r.Id), content, IntPtr.Zero);
            Send(content, Sel("release"));

            long number = ++_nextNumber;
            Sending[number] = r;
            Clicks[r.Id] = r.Click;
            Send(_center, Sel("addNotificationRequest:withCompletionHandler:"), request,
                GlobalBlock(Marshal.GetFunctionPointerForDelegate(OnAdded), number));
            return true;
        }
        catch (Exception ex)
        {
            Store.Log("macOS notification could not be sent: " + ex.Message);
            Clicks.Remove(r.Id);
            return false;
        }
    }

    /// <summary>
    /// Connects to the notification center once: a delegate for clicks (and for showing notifications while
    /// PinList is the active app), and asks for permission. False if that isn't possible.
    /// </summary>
    static bool Start()
    {
        if (_started) return _center != IntPtr.Zero;
        _started = true;
        try
        {
            // Without an app bundle, macOS throws an Objective-C exception here, which would end PinList
            var bundle = Send(objc_getClass("NSBundle"), Sel("mainBundle"));
            if (bundle == IntPtr.Zero || Send(bundle, Sel("bundleIdentifier")) == IntPtr.Zero)
            {
                Store.Log("macOS notifications: PinList isn't running from PinList.app, reminders use PinList's popup.");
                return false;
            }
            NativeLibrary.Load(UserNotifications);
            var centerClass = objc_getClass("UNUserNotificationCenter");
            _center = centerClass == IntPtr.Zero ? IntPtr.Zero : Send(centerClass, Sel("currentNotificationCenter"));
            if (_center == IntPtr.Zero)
            {
                Store.Log("macOS notifications: no notification center, reminders use PinList's popup.");
                return false;
            }

            var cls = objc_allocateClassPair(objc_getClass("NSObject"), "PinListNotificationDelegate", IntPtr.Zero);
            class_addMethod(cls, Sel("userNotificationCenter:willPresentNotification:withCompletionHandler:"),
                Marshal.GetFunctionPointerForDelegate(OnWillPresent), "v@:@@@?");
            class_addMethod(cls, Sel("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:"),
                Marshal.GetFunctionPointerForDelegate(OnDidReceive), "v@:@@@?");
            var protocol = objc_getProtocol("UNUserNotificationCenterDelegate");
            if (protocol != IntPtr.Zero) class_addProtocol(cls, protocol);
            objc_registerClassPair(cls);
            var del = Send(Send(cls, Sel("alloc")), Sel("init")); // never released: the center keeps a weak reference
            Send(_center, Sel("setDelegate:"), del);

            _permission = Permission.Asking;
            SendOptions(_center, Sel("requestAuthorizationWithOptions:completionHandler:"), Alert | Sound,
                GlobalBlock(Marshal.GetFunctionPointerForDelegate(OnAuthorized), 0));
            return true;
        }
        catch (Exception ex)
        {
            Store.Log("macOS notifications: " + ex.Message);
            _center = IntPtr.Zero;
            return false;
        }
    }

    /// <summary>
    /// Reads PinList's notification settings (allowed, style, Notification Center); the answer goes to
    /// ReminderSettings, which Settings shows.
    /// </summary>
    public static void CheckSettings()
    {
        if (!OperatingSystem.IsMacOS() || !Start()) return;
        try
        {
            if (_settingsBlock == IntPtr.Zero) _settingsBlock = GlobalBlock(Marshal.GetFunctionPointerForDelegate(OnSettings), 0);
            Send(_center, Sel("getNotificationSettingsWithCompletionHandler:"), _settingsBlock);
        }
        catch (Exception ex) { Store.Log("macOS notification settings: " + ex.Message); }
    }

    /// <summary>The settings arrived (on a background thread).</summary>
    static void SettingsRead(IntPtr block, IntPtr settings)
    {
        string? problem = ReminderSettings.Describe(
            SendGetLong(settings, Sel("authorizationStatus")),
            SendGetLong(settings, Sel("notificationCenterSetting")),
            SendGetLong(settings, Sel("alertStyle")));
        Dispatcher.UIThread.Post(() => ReminderSettings.Report(problem));
    }

    /// <summary>macOS answered the permission question (on a background thread).</summary>
    static void Authorized(IntPtr block, byte granted, IntPtr error)
    {
        bool ok = granted != 0;
        string why = Describe(error);
        Dispatcher.UIThread.Post(() =>
        {
            _permission = ok ? Permission.Granted : Permission.Denied;
            CheckSettings(); // Settings shows what's off (e.g. not in Notification Center)
            if (!ok)
                Store.Log("macOS notifications aren't allowed for PinList" + (why.Length > 0 ? $" ({why})" : "")
                          + ": reminders use PinList's popup. Check System Settings → Notifications → PinList.");
            var waiting = Waiting.ToArray();
            Waiting.Clear();
            foreach (var r in waiting)
                if (!ok || !Add(r)) ReminderPopup.Show(r.Title, r.Text, r.Click);
        });
    }

    /// <summary>macOS took the notification, or said why not (on a background thread): then the popup shows it.</summary>
    static void Added(IntPtr block, IntPtr error)
    {
        long number = Marshal.ReadInt64(block, 32);
        string why = error == IntPtr.Zero ? "" : Describe(error);
        // the block isn't freed: macOS may still look at it after this returns (40 bytes per reminder)
        Dispatcher.UIThread.Post(() =>
        {
            if (!Sending.Remove(number, out var r) || error == IntPtr.Zero) return;
            Store.Log($"macOS didn't show a reminder ({why}): PinList's popup shows it.");
            Clicks.Remove(r.Id);
            ReminderPopup.Show(r.Title, r.Text, r.Click);
        });
    }

    /// <summary>PinList is the active app: show the notification anyway (macOS would hide it otherwise).</summary>
    static void WillPresentNotification(IntPtr self, IntPtr cmd, IntPtr center, IntPtr notification, IntPtr completion) =>
        Invoke<PresentationInvoke>(completion)(completion, Banner | List | Sound);

    /// <summary>The notification was clicked (or closed): a click runs the reminder's action on the UI thread.</summary>
    static void DidReceiveResponse(IntPtr self, IntPtr cmd, IntPtr center, IntPtr response, IntPtr completion)
    {
        try
        {
            var action = ToString(Send(response, Sel("actionIdentifier")));
            var request = Send(Send(response, Sel("notification")), Sel("request"));
            var id = ToString(Send(request, Sel("identifier")));
            bool clicked = action == "com.apple.UNNotificationDefaultActionIdentifier";
            if (id != null)
                Dispatcher.UIThread.Post(() =>
                {
                    if (Clicks.Remove(id, out var click) && clicked) click();
                });
        }
        finally { Invoke<DoneInvoke>(completion)(completion); }
    }

    static T Invoke<T>(IntPtr block) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(block, 16));

    static IntPtr _descriptor;

    /// <summary>A block without captured values (plus a number for the callback); macOS never copies or frees it.</summary>
    static IntPtr GlobalBlock(IntPtr invoke, long number)
    {
        if (_descriptor == IntPtr.Zero)
        {
            _descriptor = Marshal.AllocHGlobal(16);
            Marshal.WriteInt64(_descriptor, 0, 0);  // reserved
            Marshal.WriteInt64(_descriptor, 8, 40); // size of the block
        }
        var isa = NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "_NSConcreteGlobalBlock");
        var block = Marshal.AllocHGlobal(40);
        Marshal.WriteIntPtr(block, 0, isa);
        Marshal.WriteInt32(block, 8, BlockIsGlobal);
        Marshal.WriteInt32(block, 12, 0);
        Marshal.WriteIntPtr(block, 16, invoke);
        Marshal.WriteIntPtr(block, 24, _descriptor);
        Marshal.WriteInt64(block, 32, number);
        return block;
    }

    /// <summary>An NSError as "message (domain code)" for the log.</summary>
    static string Describe(IntPtr error)
    {
        if (error == IntPtr.Zero) return "";
        try
        {
            var message = ToString(Send(error, Sel("localizedDescription")));
            var domain = ToString(Send(error, Sel("domain")));
            long code = SendGetLong(error, Sel("code"));
            return $"{message} ({domain} {code})";
        }
        catch (Exception ex) { return ex.Message; }
    }

    static IntPtr Sel(string name) => sel_registerName(name);

    static IntPtr NSString(string s) => SendString(objc_getClass("NSString"), Sel("stringWithUTF8String:"), s);

    static string? ToString(IntPtr nsString) =>
        nsString == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(nsString, Sel("UTF8String")));
}
#endif
