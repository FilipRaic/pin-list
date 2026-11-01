using System.Threading;
using System.Threading.Tasks;

namespace PinList.Sync;

/// <summary>The signed-in Google account (from Google's userinfo endpoint).</summary>
public sealed record GoogleAccount(string UserId, string Email);

/// <summary>
/// Google sign-in on one platform: loopback + PKCE on the desktop, Play services on Android.
/// Scopes are always exactly drive.appdata, openid and userinfo.email.
/// </summary>
public interface IGoogleAuth
{
    /// <summary>False when this build or device can't sync (no client id, no Google Play).</summary>
    bool IsAvailable { get; }

    /// <summary>Why sync isn't available, for the menu.</summary>
    string UnavailableText { get; }

    /// <summary>Interactive sign-in. Null if the user cancelled.</summary>
    Task<GoogleAccount?> SignIn(CancellationToken ct);

    /// <summary>A valid access token without asking the user. Null if the user must sign in again.</summary>
    Task<string?> AccessToken(CancellationToken ct);

    /// <summary>Revokes access at Google (best effort) and forgets the stored sign-in.</summary>
    Task SignOut();
}

/// <summary>Where the desktop keeps the refresh token (DPAPI on Windows, Keychain on macOS).</summary>
public interface ITokenStore
{
    string? Read();
    void Write(string refreshToken);
    void Delete();
}
