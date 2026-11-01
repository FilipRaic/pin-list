using System;
using System.IO;
using System.Text.Json;

namespace PinList;

/// <summary>
/// PinList's Google sign-in client for sync on Windows and macOS (the "Desktop app" client, see GOOGLE-REQUIREMENTS.md).
/// It comes from google-client.json, built into the app as an embedded resource when the file exists: locally from
/// src/PinList/google-client.json (not in git), in releases written from the GitHub secrets.
/// </summary>
public sealed record GoogleClient(string ClientId, string ClientSecret)
{
    internal const string ResourceName = "PinList.google-client.json";

    static readonly Lazy<GoogleClient?> _builtIn =
        new(() => Load(typeof(GoogleClient).Assembly.GetManifestResourceStream(ResourceName)));

    /// <summary>The client built into this app, or null when the build has none (sync isn't available then).</summary>
    public static GoogleClient? BuiltIn => _builtIn.Value;

    /// <summary>Reads google-client.json. Null if there's no file; null and a log entry if it's incomplete or broken.</summary>
    internal static GoogleClient? Load(Stream? stream)
    {
        if (stream == null) return null;
        using (stream)
        {
            try
            {
                using var json = JsonDocument.Parse(stream);
                var id = Text(json.RootElement, "clientId");
                var secret = Text(json.RootElement, "clientSecret");
                if (id != null && secret != null) return new GoogleClient(id, secret);
                Store.Log("google-client.json needs a clientId and a clientSecret; sync is off in this build.");
            }
            catch (JsonException ex)
            {
                Store.Log($"google-client.json can't be read, sync is off in this build: {ex.Message}");
            }
            return null;
        }
    }

    /// <summary>A non-empty text value of the root object, trimmed; null otherwise.</summary>
    static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String && value.GetString()!.Trim() is { Length: > 0 } text
            ? text
            : null;
}
