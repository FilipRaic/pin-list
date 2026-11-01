using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using PinList.Sync;

namespace PinList;

/// <summary>Where the desktop app keeps the Google refresh token (never in data.json, never exported).</summary>
public static class TokenStores
{
    /// <summary>DPAPI-encrypted google-token.dat on Windows, the login Keychain on macOS.</summary>
    [ExcludeFromCodeCoverage(Justification = "Chooses the operating system's secret storage.")]
    public static ITokenStore ForThisOs(string dataDir)
    {
#if WINDOWS
        return new DpapiTokenStore(Path.Combine(dataDir, "google-token.dat"));
#else
        return OperatingSystem.IsMacOS() ? new KeychainTokenStore() : new MemoryOnlyTokenStore();
#endif
    }
}

#if WINDOWS
/// <summary>The token encrypted for the current Windows user (DPAPI), in the data folder.</summary>
[ExcludeFromCodeCoverage(Justification = "Calls the operating system's secret storage.")]
sealed class DpapiTokenStore(string path) : ITokenStore
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PinList Google sync");

    public string? Read()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var plain = System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is IOException or System.Security.Cryptography.CryptographicException)
        {
            Store.Log("google-token.dat can't be read; sign in again: " + ex.GetType().Name);
            return null;
        }
    }

    public void Write(string refreshToken)
    {
        var data = System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(refreshToken), Entropy,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    public void Delete()
    {
        try { File.Delete(path); }
        catch (IOException ex) { Store.Log("google-token.dat can't be deleted: " + ex.Message); }
    }
}
#else
/// <summary>The token in the user's login Keychain (service "PinList", account "google-refresh-token").</summary>
[ExcludeFromCodeCoverage(Justification = "Calls the operating system's secret storage.")]
sealed class KeychainTokenStore : ITokenStore
{
    const string Lib = "/System/Library/Frameworks/Security.framework/Security";
    const string Service = "PinList";
    const string Account = "google-refresh-token";
    const int ItemNotFound = -25300;

    [DllImport(Lib)]
    static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength,
        byte[] account, uint passwordLength, byte[] password, IntPtr itemRef);

    [DllImport(Lib)]
    static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength,
        byte[] account, out uint passwordLength, out IntPtr password, out IntPtr itemRef);

    [DllImport(Lib)]
    static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attributes, uint length, byte[] data);

    [DllImport(Lib)]
    static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(Lib)]
    static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    static extern void CFRelease(IntPtr cf);

    static readonly byte[] ServiceBytes = Encoding.UTF8.GetBytes(Service);
    static readonly byte[] AccountBytes = Encoding.UTF8.GetBytes(Account);

    /// <summary>Finds the item; returns its password (or null) and the item to modify or delete (release it).</summary>
    static string? Find(out IntPtr item)
    {
        int status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)ServiceBytes.Length, ServiceBytes,
            (uint)AccountBytes.Length, AccountBytes, out uint length, out IntPtr data, out item);
        if (status != 0)
        {
            if (status != ItemNotFound) Store.Log($"Keychain: reading the sign-in failed ({status}).");
            item = IntPtr.Zero;
            return null;
        }
        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally { SecKeychainItemFreeContent(IntPtr.Zero, data); }
    }

    public string? Read()
    {
        var token = Find(out var item);
        if (item != IntPtr.Zero) CFRelease(item);
        return token;
    }

    public void Write(string refreshToken)
    {
        var bytes = Encoding.UTF8.GetBytes(refreshToken);
        Find(out var item);
        int status = item != IntPtr.Zero
            ? SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)bytes.Length, bytes)
            : SecKeychainAddGenericPassword(IntPtr.Zero, (uint)ServiceBytes.Length, ServiceBytes, (uint)AccountBytes.Length,
                AccountBytes, (uint)bytes.Length, bytes, IntPtr.Zero);
        if (item != IntPtr.Zero) CFRelease(item);
        if (status != 0) Store.Log($"Keychain: saving the sign-in failed ({status}).");
    }

    public void Delete()
    {
        Find(out var item);
        if (item == IntPtr.Zero) return;
        SecKeychainItemDelete(item);
        CFRelease(item);
    }
}

/// <summary>Other systems (Linux while developing): the sign-in lasts until the app exits.</summary>
[ExcludeFromCodeCoverage(Justification = "Only used on systems PinList doesn't ship for.")]
sealed class MemoryOnlyTokenStore : ITokenStore
{
    string? _token;
    public string? Read() => _token;
    public void Write(string refreshToken) => _token = refreshToken;
    public void Delete() => _token = null;
}
#endif
