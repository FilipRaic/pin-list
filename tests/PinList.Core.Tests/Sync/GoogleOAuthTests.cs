using System.Net;
using System.Security.Cryptography;
using System.Text;
using PinList.Sync;
using Xunit;

namespace PinList.Tests.Sync;

public class GoogleOAuthTests : StoreTestBase
{
    readonly FakeHttp _http = new();
    GoogleOAuth OAuth(string? secret = "sec") => new(new HttpClient(_http), "cid", secret);
    static CancellationToken Ct => CancellationToken.None;

    static Dictionary<string, string> Query(string url) =>
        new Uri(url).Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

    static Dictionary<string, string> Form(string body) =>
        body.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

    [Fact]
    public void Pkce_challenge_is_s256_of_the_verifier()
    {
        var (verifier, challenge) = GoogleOAuth.NewPkce();
        Assert.Equal(64, verifier.Length);
        Assert.Matches("^[A-Za-z0-9._~-]+$", verifier);
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, challenge);
        Assert.NotEqual(verifier, GoogleOAuth.NewPkce().verifier);
    }

    [Fact]
    public void State_is_random()
    {
        var s = GoogleOAuth.NewState();
        Assert.Matches("^[A-Za-z0-9_-]{22}$", s);
        Assert.NotEqual(s, GoogleOAuth.NewState());
    }

    [Theory]
    [InlineData(401, CloudError.Unauthorized)]
    [InlineData(502, CloudError.Server)]
    [InlineData(403, CloudError.Other)]
    public async Task User_info_errors_throw(int code, CloudError kind)
    {
        _http.Answer((HttpStatusCode)code);
        var ex = await Assert.ThrowsAsync<CloudException>(() => OAuth().UserInfo("at", Ct));
        Assert.Equal((kind, code), (ex.Kind, ex.Status));
    }

    [Fact]
    public void Authorize_url_has_exactly_the_three_scopes_and_pkce()
    {
        var url = OAuth().AuthorizeUrl("http://127.0.0.1:5000/", "chal", "st");
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        var q = Query(url);
        Assert.Equal("cid", q["client_id"]);
        Assert.Equal("http://127.0.0.1:5000/", q["redirect_uri"]);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal(new[] { "https://www.googleapis.com/auth/drive.appdata", "openid", "https://www.googleapis.com/auth/userinfo.email" },
            q["scope"].Split(' '));
        Assert.Equal("chal", q["code_challenge"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("st", q["state"]);
        Assert.Equal("offline", q["access_type"]);
        Assert.Equal("consent", q["prompt"]);
        Assert.False(q.ContainsKey("client_secret"));
    }

    [Fact]
    public async Task Exchange_posts_code_verifier_and_secret()
    {
        _http.Answer(HttpStatusCode.OK, """{ "access_token": "at", "expires_in": 3599, "refresh_token": "rt", "token_type": "Bearer" }""");
        var before = DateTime.UtcNow;
        var t = await OAuth().ExchangeCode("the code", "ver", "http://127.0.0.1:5000/", Ct);

        Assert.Equal(("at", "rt"), (t.AccessToken, t.RefreshToken));
        Assert.InRange(t.ExpiresUtc, before.AddSeconds(3599), DateTime.UtcNow.AddSeconds(3599));
        Assert.Equal(HttpMethod.Post, _http.Method(0));
        Assert.Equal("https://oauth2.googleapis.com/token", _http.Url(0));
        var f = Form(_http.Requests[0].body);
        Assert.Equal("the code", f["code"]);
        Assert.Equal("ver", f["code_verifier"]);
        Assert.Equal("sec", f["client_secret"]);
        Assert.Equal("cid", f["client_id"]);
        Assert.Equal("authorization_code", f["grant_type"]);
        Assert.Equal("http://127.0.0.1:5000/", f["redirect_uri"]);
    }

    [Fact]
    public async Task No_secret_is_not_sent()
    {
        _http.Answer(HttpStatusCode.OK, """{ "access_token": "at", "expires_in": 10 }""");
        var t = await OAuth(secret: null).ExchangeCode("c", "v", "r", Ct);
        Assert.Null(t.RefreshToken);
        Assert.DoesNotContain("client_secret", _http.Requests[0].body);
    }

    [Fact]
    public async Task Refresh_posts_the_refresh_token()
    {
        _http.Answer(HttpStatusCode.OK, """{ "access_token": "new", "expires_in": 3600 }""");
        var t = await OAuth().Refresh("rt", Ct);
        Assert.Equal("new", t!.AccessToken);
        var f = Form(_http.Requests[0].body);
        Assert.Equal(("refresh_token", "rt"), (f["grant_type"], f["refresh_token"]));
    }

    [Fact]
    public async Task Refresh_returns_null_on_invalid_grant()
    {
        _http.Answer(HttpStatusCode.BadRequest, """{ "error": "invalid_grant", "error_description": "Token has been expired or revoked." }""");
        Assert.Null(await OAuth().Refresh("rt", Ct));
    }

    [Fact]
    public async Task Other_token_errors_throw()
    {
        _http.Answer(HttpStatusCode.ServiceUnavailable);
        var ex = await Assert.ThrowsAsync<CloudException>(() => OAuth().Refresh("rt", Ct));
        Assert.Equal(CloudError.Server, ex.Kind);
        _http.Throw(new HttpRequestException("down"));
        ex = await Assert.ThrowsAsync<CloudException>(() => OAuth().ExchangeCode("c", "v", "r", Ct));
        Assert.Equal(CloudError.Offline, ex.Kind);
    }

    [Fact]
    public async Task Revoke_posts_the_token()
    {
        _http.Answer(HttpStatusCode.OK);
        await OAuth().Revoke("rt");
        Assert.Equal("https://oauth2.googleapis.com/revoke", _http.Url(0));
        Assert.Equal("rt", Form(_http.Requests[0].body)["token"]);
    }

    [Fact]
    public async Task Revoke_never_fails()
    {
        _http.Throw(new HttpRequestException("offline"));
        await OAuth().Revoke("rt");
        _http.Answer(HttpStatusCode.BadRequest);
        await OAuth().Revoke("rt");
    }

    [Fact]
    public async Task User_info_reads_sub_and_email()
    {
        _http.Answer(HttpStatusCode.OK, """{ "sub": "1234", "email": "me@example.com", "email_verified": true }""");
        var a = await OAuth().UserInfo("at", Ct);
        Assert.Equal(new GoogleAccount("1234", "me@example.com"), a);
        Assert.Equal("https://openidconnect.googleapis.com/v1/userinfo", _http.Url(0));
        Assert.Equal("at", _http.Requests[0].request.Headers.Authorization!.Parameter);
    }
}

public class LoopbackSignInTests
{
    /// <summary>Acts as the browser: opens the redirect URL with the given query once PinList "opens" it.</summary>
    static (Func<string, Task> open, Task<string> page) Browser(string query)
    {
        var page = new TaskCompletionSource<string>();
        Task Open(string url)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var client = new HttpClient();
                    await client.GetStringAsync("http://127.0.0.1:" + new Uri(url).Port + "/favicon.ico").ContinueWith(_ => { });
                    page.SetResult(await client.GetStringAsync(url + query));
                }
                catch (Exception ex) { page.SetException(ex); }
            });
            return Task.CompletedTask;
        }
        return (Open, page.Task);
    }

    [Fact]
    public async Task Loopback_returns_the_code()
    {
        var (open, page) = Browser("?state=s&code=4%2Fabc&scope=x&authuser");
        string? redirect = null;
        var code = await LoopbackSignIn.WaitForCode(open, r => { redirect = r; return r; }, "s", CancellationToken.None);
        Assert.Equal("4/abc", code);
        Assert.Matches(@"^http://127\.0\.0\.1:\d+/$", redirect);
        Assert.Contains("You're signed in to PinList. You can close this tab.", await page);
    }

    [Fact]
    public async Task Loopback_rejects_a_wrong_state()
    {
        var (open, page) = Browser("?state=other&code=abc");
        Assert.Null(await LoopbackSignIn.WaitForCode(open, r => r, "s", CancellationToken.None));
        Assert.Contains("Sign-in was cancelled.", await page);
    }

    [Fact]
    public async Task Loopback_returns_null_when_the_user_says_no()
    {
        var (open, page) = Browser("?error=access_denied&state=s");
        Assert.Null(await LoopbackSignIn.WaitForCode(open, r => r, "s", CancellationToken.None));
        Assert.Contains("Sign-in was cancelled.", await page);
    }

    [Fact]
    public async Task Loopback_returns_null_when_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var wait = LoopbackSignIn.WaitForCode(_ => Task.CompletedTask, r => r, "s", cts.Token);
        cts.Cancel();
        Assert.Null(await wait);
    }

    [Fact]
    public async Task Loopback_ignores_a_connection_that_sends_nothing()
    {
        int port = 0;
        var page = new TaskCompletionSource<string>();
        Task Open(string url)
        {
            port = new Uri(url).Port;
            _ = Task.Run(async () =>
            {
                using var idle = new System.Net.Sockets.TcpClient();
                await idle.ConnectAsync("127.0.0.1", port); // a browser's preconnect: never sends a request
                using var browser = new HttpClient();
                page.SetResult(await browser.GetStringAsync(url + "?state=s&code=abc"));
                await Task.Delay(500);
            });
            return Task.CompletedTask;
        }
        var code = await LoopbackSignIn.WaitForCode(Open, r => r, "s", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("abc", code);
        Assert.Contains("signed in", await page.Task);
    }

    [Fact]
    public async Task Loopback_takes_the_first_of_repeated_values()
    {
        var (open, page) = Browser("?state=s&code=first&code=second&state=other");
        Assert.Equal("first", await LoopbackSignIn.WaitForCode(open, r => r, "s", CancellationToken.None));
        await page;
    }
}
