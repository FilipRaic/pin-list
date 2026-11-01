using System.Net;
using System.Text;
using PinList.Sync;
using Xunit;

namespace PinList.Tests.Sync;

public class DriveCloudStoreTests
{
    readonly FakeHttp _http = new();
    string? _token = "tok";
    DriveCloudStore Store() => new(new HttpClient(_http), _ => Task.FromResult(_token));
    static CancellationToken Ct => CancellationToken.None;

    const string Page1 = """{ "nextPageToken": "p2", "files": [ { "id": "1", "name": "w-a.A.json", "modifiedTime": "2026-11-01T10:00:00.000Z" } ] }""";
    const string Page2 = """{ "files": [ { "id": "2", "name": "reset.json", "modifiedTime": "2026-11-01T11:30:00Z" } ] }""";

    [Fact]
    public async Task Lists_all_pages_of_the_app_folder()
    {
        _http.Answer(HttpStatusCode.OK, Page1);
        _http.Answer(HttpStatusCode.OK, Page2);
        var files = await Store().List(Ct);

        Assert.Equal(new[] { "w-a.A.json", "reset.json" }, files.Select(f => f.Name));
        Assert.Equal(new DateTime(2026, 11, 1, 11, 30, 0, DateTimeKind.Utc), files[1].ModifiedUtc);
        Assert.Equal(DateTimeKind.Utc, files[0].ModifiedUtc.Kind);
        Assert.StartsWith("https://www.googleapis.com/drive/v3/files?", _http.Url(0));
        Assert.Contains("spaces=appDataFolder", _http.Url(0));
        Assert.Contains("pageSize=1000", _http.Url(0));
        Assert.DoesNotContain("pageToken", _http.Url(0));
        Assert.Contains("pageToken=p2", _http.Url(1));
    }

    [Fact]
    public async Task Upload_creates_then_replaces_by_name()
    {
        var store = Store();
        _http.Answer(HttpStatusCode.OK, """{ "files": [] }""");
        _http.Answer(HttpStatusCode.OK, """{ "id": "new1" }""");
        _http.Answer(HttpStatusCode.OK, """{ "id": "new1" }""");
        await store.Upload("w-b.A.json", "{\"v\":1}", Ct);
        await store.Upload("w-b.A.json", "{\"v\":2}", Ct);

        Assert.Equal(HttpMethod.Get, _http.Method(0)); // looks for an existing file first
        Assert.Equal(HttpMethod.Post, _http.Method(1));
        Assert.Equal("https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart", _http.Url(1));
        Assert.Contains("\"parents\":[\"appDataFolder\"]", _http.Requests[1].body);
        Assert.Contains("\"name\":\"w-b.A.json\"", _http.Requests[1].body);
        Assert.Contains("{\"v\":1}", _http.Requests[1].body);
        Assert.Equal(HttpMethod.Patch, _http.Method(2));
        Assert.Equal("https://www.googleapis.com/upload/drive/v3/files/new1?uploadType=media", _http.Url(2));
        Assert.Equal("{\"v\":2}", _http.Requests[2].body);
    }

    [Fact]
    public async Task Upload_replaces_a_listed_file()
    {
        var store = Store();
        _http.Answer(HttpStatusCode.OK, Page2);
        await store.List(Ct);
        _http.Answer(HttpStatusCode.OK, "{}");
        await store.Upload("reset.json", "{}", Ct);
        Assert.Equal(HttpMethod.Patch, _http.Method(1));
        Assert.Contains("/files/2?", _http.Url(1));
    }

    [Fact]
    public async Task Upload_creates_again_when_the_file_was_deleted_elsewhere()
    {
        var store = Store();
        _http.Answer(HttpStatusCode.OK, Page2);
        await store.List(Ct);
        _http.Answer(HttpStatusCode.NotFound, """{ "error": { "code": 404 } }""");
        _http.Answer(HttpStatusCode.OK, """{ "id": "3" }""");
        await store.Upload("reset.json", "{}", Ct);
        Assert.Equal(HttpMethod.Post, _http.Method(2));
    }

    [Fact]
    public async Task Download_returns_the_body()
    {
        _http.Answer(HttpStatusCode.OK, "content", "application/octet-stream");
        var text = await Store().Download(new CloudFile("abc", "x.json", DateTime.UtcNow), Ct);
        Assert.Equal("content", text);
        Assert.Equal("https://www.googleapis.com/drive/v3/files/abc?alt=media", _http.Url(0));
    }

    [Fact]
    public async Task Delete_sends_delete()
    {
        var store = Store();
        _http.Answer(HttpStatusCode.OK, Page2);
        await store.List(Ct);
        _http.Answer(HttpStatusCode.NoContent);
        await store.Delete(new CloudFile("2", "reset.json", DateTime.UtcNow), Ct);
        Assert.Equal(HttpMethod.Delete, _http.Method(1));
        Assert.Equal("https://www.googleapis.com/drive/v3/files/2", _http.Url(1));

        // the name is free again: the next upload creates a new file
        _http.Answer(HttpStatusCode.OK, """{ "id": "9" }""");
        await store.Upload("reset.json", "{}", Ct);
        Assert.Equal(HttpMethod.Post, _http.Method(2));
    }

    [Fact]
    public async Task Sends_the_bearer_token()
    {
        _http.Answer(HttpStatusCode.OK, Page2);
        await Store().List(Ct);
        var auth = _http.Requests[0].request.Headers.Authorization!;
        Assert.Equal("Bearer", auth.Scheme);
        Assert.Equal("tok", auth.Parameter);
    }

    [Theory]
    [InlineData(401, "", CloudError.Unauthorized)]
    [InlineData(403, """{"error":{"errors":[{"reason":"rateLimitExceeded"}]}}""", CloudError.RateLimited)]
    [InlineData(403, """{"error":{"errors":[{"reason":"userRateLimitExceeded"}]}}""", CloudError.RateLimited)]
    [InlineData(403, """{"error":{"errors":[{"reason":"insufficientPermissions"}]}}""", CloudError.Unauthorized)]
    [InlineData(403, """{"error":{"errors":[{"reason":"insufficientScopes"}]}}""", CloudError.Unauthorized)]
    [InlineData(403, """{"error":{"errors":[{"reason":"forbidden"}]}}""", CloudError.Other)]
    [InlineData(429, "", CloudError.RateLimited)]
    [InlineData(500, "", CloudError.Server)]
    [InlineData(503, "", CloudError.Server)]
    [InlineData(400, "", CloudError.Other)]
    public async Task Status_codes_map_to_errors(int code, string body, CloudError expected)
    {
        _http.Answer((HttpStatusCode)code, body);
        var ex = await Assert.ThrowsAsync<CloudException>(() => Store().List(Ct));
        Assert.Equal(expected, ex.Kind);
        Assert.DoesNotContain("tok", ex.Message);
    }

    [Fact]
    public async Task No_token_is_unauthorized()
    {
        _token = null;
        var ex = await Assert.ThrowsAsync<CloudException>(() => Store().List(Ct));
        Assert.Equal(CloudError.Unauthorized, ex.Kind);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Network_failure_is_offline()
    {
        _http.Throw(new HttpRequestException("no network"));
        var ex = await Assert.ThrowsAsync<CloudException>(() => Store().List(Ct));
        Assert.Equal(CloudError.Offline, ex.Kind);

        _http.Throw(new TaskCanceledException("timeout"));
        ex = await Assert.ThrowsAsync<CloudException>(() => Store().Download(new CloudFile("1", "x", DateTime.UtcNow), Ct));
        Assert.Equal(CloudError.Offline, ex.Kind);
    }

    [Fact]
    public async Task Cancelling_is_not_an_error()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _http.Answers.Enqueue(_ => throw new TaskCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store().List(cts.Token));
    }

    [Fact]
    public async Task Delete_works_before_any_list()
    {
        _http.Answer(HttpStatusCode.NoContent);
        await Store().Delete(new CloudFile("7", "x.json", DateTime.UtcNow), Ct);
        Assert.Equal(HttpMethod.Delete, _http.Method(0));
    }

    [Fact]
    public async Task Answer_that_isnt_json_is_an_error()
    {
        _http.Answer(HttpStatusCode.OK, "<html>captive portal</html>", "text/html");
        var ex = await Assert.ThrowsAsync<CloudException>(() => Store().List(Ct));
        Assert.Equal(CloudError.Offline, ex.Kind);
    }
}
