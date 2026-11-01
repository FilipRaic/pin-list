using System.Text;
using Avalonia.Headless.XUnit;
using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>google-client.json, the Google sign-in client built into the desktop app for sync.</summary>
public class GoogleClientTests : StoreTestBase
{
    static Stream Json(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [AvaloniaFact]
    public void Reads_the_client_id_and_secret()
    {
        var client = GoogleClient.Load(Json("""{ "clientId": " 123-abc.apps.googleusercontent.com ", "clientSecret": "GOCSPX-x\"y" }"""));
        Assert.Equal(new GoogleClient("123-abc.apps.googleusercontent.com", "GOCSPX-x\"y"), client);
    }

    [AvaloniaFact]
    public void No_file_means_no_sync() => Assert.Null(GoogleClient.Load(null));

    [AvaloniaTheory]
    [InlineData("""{ "clientId": "id" }""")]
    [InlineData("""{ "clientId": "  ", "clientSecret": "secret" }""")]
    [InlineData("""{ "clientId": 5, "clientSecret": "secret" }""")]
    [InlineData("""[]""")]
    [InlineData("""not json""")]
    public void A_broken_file_means_no_sync_and_is_logged(string text)
    {
        Assert.Null(GoogleClient.Load(Json(text)));
        Assert.Contains("google-client.json", File.ReadAllText(Path.Combine(Dir, "log.txt")));
    }

    [AvaloniaFact]
    public void The_built_in_client_can_be_read()
    {
        // CI builds have no google-client.json (null); a developer's build may have one. Either way it must not throw.
        var client = GoogleClient.BuiltIn;
        if (client != null) Assert.NotEmpty(client.ClientId);
    }
}
