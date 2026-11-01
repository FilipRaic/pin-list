using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PinList.Sync;

/// <summary>Result of a token request. ExpiresUtc is when the access token stops working.</summary>
public sealed record TokenResult(string AccessToken, string? RefreshToken, DateTime ExpiresUtc);

/// <summary>Google's OAuth 2.0 endpoints for an installed app (PKCE), with plain HttpClient.</summary>
public sealed class GoogleOAuth(HttpClient http, string clientId, string? clientSecret)
{
    public static readonly string[] Scopes =
        { "https://www.googleapis.com/auth/drive.appdata", "openid", "https://www.googleapis.com/auth/userinfo.email" };

    const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";

    const string VerifierChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    /// <summary>A PKCE pair (RFC 7636, S256): a random 64-character verifier and its challenge.</summary>
    public static (string verifier, string challenge) NewPkce()
    {
        var verifier = new string(RandomNumberGenerator.GetItems<char>(VerifierChars, 64));
        return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    /// <summary>A random value for the state parameter.</summary>
    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public string AuthorizeUrl(string redirectUri, string challenge, string state)
    {
        var q = new (string, string)[]
        {
            ("client_id", clientId), ("redirect_uri", redirectUri), ("response_type", "code"),
            ("scope", string.Join(' ', Scopes)), ("code_challenge", challenge), ("code_challenge_method", "S256"),
            ("state", state), ("access_type", "offline"), ("prompt", "consent"),
        };
        return AuthEndpoint + "?" + string.Join("&", q.Select(p => p.Item1 + "=" + Uri.EscapeDataString(p.Item2)));
    }

    public async Task<TokenResult> ExchangeCode(string code, string verifier, string redirectUri, CancellationToken ct) =>
        (await Token(new()
        {
            ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = redirectUri, ["grant_type"] = "authorization_code",
        }, ct))!;

    /// <summary>A new access token. Null when the refresh token was revoked or expired (the user must sign in again).</summary>
    public Task<TokenResult?> Refresh(string refreshToken, CancellationToken ct) =>
        Token(new() { ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token" }, ct);

    async Task<TokenResult?> Token(Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = clientId;
        if (clientSecret != null) form["client_secret"] = clientSecret;
        var (code, body) = await Send(new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(form) }, ct);
        if (code == 400 && body.Contains("invalid_grant", StringComparison.Ordinal)) return null;
        Check(code, "token");
        var json = JsonNode.Parse(body)!;
        return new TokenResult((string)json["access_token"]!, (string?)json["refresh_token"],
            DateTime.UtcNow.AddSeconds((double)json["expires_in"]!));
    }

    /// <summary>Revokes a token at Google. Never fails: signing out must work offline too.</summary>
    public async Task Revoke(string token)
    {
        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
            var (code, _) = await Send(new HttpRequestMessage(HttpMethod.Post, RevokeEndpoint) { Content = form }, CancellationToken.None);
            if (code != 200) Store.Log($"Google sign-out: revoking access answered HTTP {code}.");
        }
        catch (CloudException ex) { Store.Log("Google sign-out: couldn't revoke access: " + ex.Message); }
    }

    public async Task<GoogleAccount> UserInfo(string accessToken, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var (code, body) = await Send(request, ct);
        Check(code, "userinfo");
        var json = JsonNode.Parse(body)!;
        return new GoogleAccount((string)json["sub"]!, (string)json["email"]!);
    }

    async Task<(int code, string body)> Send(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using (request)
            using (var response = await http.SendAsync(request, ct))
                return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new CloudException(CloudError.Offline, "Can't reach Google: " + ex.GetType().Name);
        }
    }

    static void Check(int code, string what)
    {
        if (code is >= 200 and < 300) return;
        throw new CloudException(code switch { 401 => CloudError.Unauthorized, >= 500 => CloudError.Server, _ => CloudError.Other },
            $"Google {what} answered HTTP {code}") { Status = code };
    }
}
