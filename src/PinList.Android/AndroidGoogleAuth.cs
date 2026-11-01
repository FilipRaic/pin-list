using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Gms.Auth.Api.Identity;
using Android.Gms.Common;
using Android.Gms.Common.Apis;
using Android.Gms.Extensions;
using PinList.Sync;

namespace PinList.Android;

/// <summary>
/// Google sign-in on Android (spec 9.7): the Play services Authorization API with PinList's three scopes. Android
/// recognizes PinList by its package name and signing key (GOOGLE-REQUIREMENTS.md), so there's no client id here.
/// PinList keeps no token: it asks Play services for a fresh access token before each sync. In the background
/// (no screen) it only asks silently.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Calls Google Play services on the phone.")]
public sealed class AndroidGoogleAuth(Context context) : IGoogleAuth
{
    const int RequestCode = 4711;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly GoogleOAuth _oauth = new(Http, "", null); // only for the account's e-mail and for revoking

    TaskCompletionSource<string?>? _pending;
    string? _lastToken;

    /// <summary>The visible screen, for Google's consent page. Null while PinList has no screen (background sync).</summary>
    public static Activity? CurrentActivity { get; set; }

    public bool IsAvailable =>
        GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(context) == ConnectionResult.Success;

    public string UnavailableText => "Sync isn't available on this phone.";

    static AuthorizationRequest Request() =>
        AuthorizationRequest.InvokeBuilder()
            .SetRequestedScopes(GoogleOAuth.Scopes.Select(s => new Scope(s)).ToList())
            .Build();

    public async Task<GoogleAccount?> SignIn(CancellationToken ct)
    {
        var token = await Authorize(interactive: true, ct);
        Trace(token == null ? "sign-in: no access token (cancelled or refused)" : "sign-in: got an access token, asking for the account");
        if (token == null) return null;
        var account = await _oauth.UserInfo(token, ct);
        Trace("sign-in: account id received" + (string.IsNullOrEmpty(account.Email) ? ", but no email" : ""));
        return account;
    }

    /// <summary>Each step of the sign-in, in log.txt and in Android's log (adb logcat -s PinList). No tokens.</summary>
    static void Trace(string step)
    {
        Store.Log("Google " + step);
        global::Android.Util.Log.Info("PinList", step);
    }

    public Task<string?> AccessToken(CancellationToken ct) => Authorize(interactive: false, ct);

    async Task<string?> Authorize(bool interactive, CancellationToken ct)
    {
        AuthorizationResult result;
        try
        {
            result = await Identity.GetAuthorizationClient(context).Authorize(Request()).AsAsync<AuthorizationResult>();
        }
        catch (ApiException ex)
        {
            Store.Log($"Google authorization failed (status {ex.StatusCode}).");
            // no network or Google busy: try again later; anything else means signing in again
            if (ex.StatusCode is CommonStatusCodes.NetworkError or CommonStatusCodes.Timeout or CommonStatusCodes.InternalError)
                throw new CloudException(CloudError.Offline, "Google Play services can't reach Google.");
            return null;
        }

        Trace($"authorize (interactive: {interactive}): needs the user: {result.HasResolution}, token: {result.AccessToken != null}, " +
              $"granted scopes: {string.Join(" ", result.GrantedScopes ?? new System.Collections.Generic.List<string>())}");
        if (!result.HasResolution) return _lastToken = result.AccessToken;
        // Google needs the user (first sign-in, or access was removed): only with a screen and when asked to
        if (!interactive || CurrentActivity is not { } activity || result.PendingIntent is not { } intent) return null;

        _pending = new TaskCompletionSource<string?>();
        using var cancel = ct.Register(() => _pending.TrySetResult(null));
        activity.StartIntentSenderForResult(intent.IntentSender, RequestCode, null, 0, 0, 0, null);
        Trace("authorize: Google's page opened");
        return await _pending.Task;
    }

    /// <summary>The consent page closed (called by MainActivity). True if it was this sign-in's result.</summary>
    public bool OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != RequestCode || _pending == null) return false;
        Trace($"authorize: Google's page closed with {resultCode}, data: {data != null}");
        string? token = null;
        if (resultCode == Result.Ok && data != null)
        {
            try { token = Identity.GetAuthorizationClient(context).GetAuthorizationResultFromIntent(data).AccessToken; }
            catch (ApiException ex) { Trace($"authorize: the answer couldn't be read (status {ex.StatusCode})"); }
        }
        _pending.TrySetResult(_lastToken = token);
        return true;
    }

    /// <summary>Removes PinList's access at Google (spec 5.7).</summary>
    public async Task SignOut()
    {
        var token = _lastToken ?? await AccessToken(CancellationToken.None);
        if (token != null) await _oauth.Revoke(token);
        _lastToken = null;
    }
}
