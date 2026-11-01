using System.Net;
using PinList.Sync;
using PinList.Tests.Sync;
using Xunit;

namespace PinList.Tests;

/// <summary>Keeps the refresh token in memory.</summary>
sealed class MemoryTokenStore : ITokenStore
{
    public string? Token;
    public string? Read() => Token;
    public void Write(string refreshToken) => Token = refreshToken;
    public void Delete() => Token = null;
}

public class DesktopGoogleAuthTests : StoreTestBase
{
    readonly FakeHttp _http = new();
    readonly MemoryTokenStore _tokens = new();
    readonly List<string> _opened = new();
    static readonly GoogleClient Client = new("cid", "secret");
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The browser: the user signs in (or says no), Google redirects to PinList's loopback address.</summary>
    Func<string, Task> Browser(bool userSaysYes = true) => url =>
    {
        _opened.Add(url);
        var q = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        var answer = userSaysYes ? $"?code=the-code&state={q["state"]}" : $"?error=access_denied&state={q["state"]}";
        _ = Task.Run(async () =>
        {
            using var browser = new HttpClient();
            await browser.GetStringAsync(q["redirect_uri"] + answer);
        });
        return Task.CompletedTask;
    };

    DesktopGoogleAuth Auth(GoogleClient? client = null, bool userSaysYes = true) =>
        new(client ?? Client, _tokens, new HttpClient(_http), Browser(userSaysYes));

    void TokenAnswer(string access, string? refresh = "rt", int expiresIn = 3600) =>
        _http.Answer(HttpStatusCode.OK, $$"""{ "access_token": "{{access}}", "expires_in": {{expiresIn}}{{(refresh == null ? "" : $", \"refresh_token\": \"{refresh}\"")}} }""");

    [Fact]
    public async Task No_client_means_unavailable()
    {
        var auth = new DesktopGoogleAuth(null, _tokens, new HttpClient(_http), Browser());
        Assert.False(auth.IsAvailable);
        Assert.Equal("Sync isn't available in this build.", auth.UnavailableText);
        Assert.Null(await auth.SignIn(Ct));
        Assert.Null(await auth.AccessToken(Ct));
        Assert.Empty(_opened);
    }

    [Fact]
    public async Task Sign_in_stores_the_refresh_token_and_returns_the_account()
    {
        TokenAnswer("at1");
        _http.Answer(HttpStatusCode.OK, """{ "sub": "42", "email": "me@example.com" }""");
        var auth = Auth();
        Assert.True(auth.IsAvailable);
        var account = await auth.SignIn(Ct);

        Assert.Equal(new GoogleAccount("42", "me@example.com"), account);
        Assert.Equal("rt", _tokens.Token);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", _opened.Single());
        Assert.Contains("code_verifier", _http.Requests[0].body);
        Assert.Contains("client_secret=secret", _http.Requests[0].body);
        Assert.Equal("at1", await auth.AccessToken(Ct)); // from sign-in, no refresh needed
        Assert.Equal(2, _http.Requests.Count);
    }

    [Fact]
    public async Task Access_token_is_refreshed_and_cached()
    {
        _tokens.Token = "rt";
        var auth = Auth();
        TokenAnswer("fresh", refresh: null);
        Assert.Equal("fresh", await auth.AccessToken(Ct));
        Assert.Equal("fresh", await auth.AccessToken(Ct));
        Assert.Single(_http.Requests);
        Assert.Contains("grant_type=refresh_token", _http.Requests[0].body);

        // less than a minute left: refreshed again
        var soon = Auth();
        TokenAnswer("short", refresh: null, expiresIn: 30);
        TokenAnswer("next", refresh: "rotated");
        Assert.Equal("short", await soon.AccessToken(Ct));
        Assert.Equal("next", await soon.AccessToken(Ct));
        Assert.Equal("rotated", _tokens.Token);
    }

    [Fact]
    public async Task No_stored_token_means_signed_out()
    {
        Assert.Null(await Auth().AccessToken(Ct));
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Revoked_refresh_token_means_sign_in_again()
    {
        _tokens.Token = "rt";
        _http.Answer(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""");
        Assert.Null(await Auth().AccessToken(Ct));
        Assert.Null(_tokens.Token);
    }

    [Fact]
    public async Task Sign_out_revokes_and_deletes_the_token()
    {
        _tokens.Token = "rt";
        _http.Answer(HttpStatusCode.OK);
        var auth = Auth();
        await auth.SignOut();
        Assert.Equal("https://oauth2.googleapis.com/revoke", _http.Url(0));
        Assert.Contains("token=rt", _http.Requests[0].body);
        Assert.Null(_tokens.Token);

        await auth.SignOut(); // nothing left to revoke
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Cancelled_sign_in_returns_null()
    {
        Assert.Null(await Auth(userSaysYes: false).SignIn(Ct));
        Assert.Null(_tokens.Token);
        Assert.Empty(_http.Requests);

        using var cts = new CancellationTokenSource();
        var auth = new DesktopGoogleAuth(Client, _tokens, new HttpClient(_http), _ => Task.CompletedTask);
        var waiting = auth.SignIn(cts.Token);
        cts.Cancel(); // the user pressed Cancel in PinList
        Assert.Null(await waiting);
    }

    [Fact]
    public void Token_store_for_this_system_keeps_the_token_encrypted()
    {
        var store = TokenStores.ForThisOs(Dir);
        store.Delete(); // nothing there yet: no error
        Assert.Null(store.Read());
        store.Write("refresh-secret");
        Assert.Equal("refresh-secret", store.Read());
        Assert.DoesNotContain("refresh-secret", File.ReadAllText(Path.Combine(Dir, "google-token.dat")));
        store.Delete();
        Assert.Null(store.Read());
        Assert.False(File.Exists(Path.Combine(Dir, "google-token.dat")));
    }
}
