using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PinList.Sync;

namespace PinList;

/// <summary>
/// Google sign-in on Windows and macOS (spec 9.6): the browser opens Google's page, Google redirects back to
/// PinList on 127.0.0.1 (PKCE). The refresh token is kept by the system's secret storage (ITokenStore); access
/// tokens only in memory, until a minute before they expire.
/// </summary>
public sealed class DesktopGoogleAuth(GoogleClient? client, ITokenStore tokens, HttpClient http, Func<string, Task> openBrowser) : IGoogleAuth
{
    readonly GoogleOAuth? _oauth = client == null ? null : new GoogleOAuth(http, client.ClientId, client.ClientSecret);
    string? _access;
    DateTime _expires;

    public bool IsAvailable => _oauth != null;
    public string UnavailableText => "Sync isn't available in this build.";

    public async Task<GoogleAccount?> SignIn(CancellationToken ct)
    {
        if (_oauth == null) return null;
        var (verifier, challenge) = GoogleOAuth.NewPkce();
        var state = GoogleOAuth.NewState();
        string redirect = "";
        var code = await LoopbackSignIn.WaitForCode(openBrowser,
            r => _oauth.AuthorizeUrl(redirect = r, challenge, state), state, ct);
        if (code == null) return null;

        var token = await _oauth.ExchangeCode(code, verifier, redirect, ct);
        Keep(token);
        return await _oauth.UserInfo(token.AccessToken, ct);
    }

    public async Task<string?> AccessToken(CancellationToken ct)
    {
        if (_oauth == null) return null;
        if (_access != null && DateTime.UtcNow < _expires.AddSeconds(-60)) return _access;
        var refresh = tokens.Read();
        if (refresh == null) return null;

        var token = await _oauth.Refresh(refresh, ct);
        if (token == null)
        {
            tokens.Delete(); // revoked or expired: the user has to sign in again
            _access = null;
            return null;
        }
        Keep(token);
        return _access;
    }

    void Keep(TokenResult token)
    {
        _access = token.AccessToken;
        _expires = token.ExpiresUtc;
        if (token.RefreshToken != null) tokens.Write(token.RefreshToken);
    }

    public async Task SignOut()
    {
        var token = tokens.Read() ?? _access;
        if (token != null && _oauth != null) await _oauth.Revoke(token);
        tokens.Delete();
        _access = null;
    }
}
