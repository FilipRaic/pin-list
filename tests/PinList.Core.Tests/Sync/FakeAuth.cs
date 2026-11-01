using PinList.Sync;

namespace PinList.Tests.Sync;

/// <summary>Google sign-in answered by the test.</summary>
sealed class FakeAuth : IGoogleAuth
{
    public bool IsAvailable { get; set; } = true;
    public string UnavailableText { get; set; } = "Sync isn't available in this build.";

    /// <summary>Who signs in next; null means the user cancels.</summary>
    public GoogleAccount? NextAccount = new("user-1", "me@example.com");

    /// <summary>False when Google has revoked access (AccessToken then returns null).</summary>
    public bool TokenValid = true;

    public int SignIns, SignOuts;

    /// <summary>Thrown by the next sign-in (e.g. no network).</summary>
    public Exception? SignInError { get; set; }

    public Task<GoogleAccount?> SignIn(CancellationToken ct)
    {
        SignIns++;
        if (SignInError is { } e) { SignInError = null; throw e; }
        if (NextAccount != null) TokenValid = true;
        return Task.FromResult(NextAccount);
    }

    public Task<string?> AccessToken(CancellationToken ct) => Task.FromResult(TokenValid ? "token" : null);

    public Task SignOut()
    {
        SignOuts++;
        return Task.CompletedTask;
    }
}
