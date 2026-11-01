using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PinList.Sync;

/// <summary>
/// PinList's files in Google Drive's hidden appDataFolder, through the Drive REST API v3 with plain HttpClient
/// (works the same on Windows, macOS and Android, and in trimmed single-file builds).
/// </summary>
public sealed class DriveCloudStore(HttpClient http, Func<CancellationToken, Task<string?>> accessToken) : ICloudStore
{
    const string Api = "https://www.googleapis.com/drive/v3/files";
    const string UploadApi = "https://www.googleapis.com/upload/drive/v3/files";

    /// <summary>File name → id, from the last List (each name is used once in the folder).</summary>
    Dictionary<string, string>? _ids;

    public async Task<IReadOnlyList<CloudFile>> List(CancellationToken ct)
    {
        var files = new List<CloudFile>();
        string? page = null;
        do
        {
            var url = $"{Api}?spaces=appDataFolder&fields=nextPageToken,files(id,name,modifiedTime)&pageSize=1000"
                      + (page == null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            var json = ParseAnswer(await Send(HttpMethod.Get, url, null, ct));
            foreach (var f in json["files"]!.AsArray())
                files.Add(new CloudFile((string)f!["id"]!, (string)f["name"]!,
                    DateTime.Parse((string)f["modifiedTime"]!, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)));
            page = (string?)json["nextPageToken"];
        } while (page != null);

        _ids = new Dictionary<string, string>();
        foreach (var f in files) _ids[f.Name] = f.Id;
        return files;
    }

    public Task<string> Download(CloudFile f, CancellationToken ct) =>
        Send(HttpMethod.Get, $"{Api}/{Uri.EscapeDataString(f.Id)}?alt=media", null, ct);

    public async Task Upload(string name, string json, CancellationToken ct)
    {
        if (_ids == null) await List(ct);
        if (_ids!.TryGetValue(name, out var id))
        {
            try
            {
                await Send(HttpMethod.Patch, $"{UploadApi}/{Uri.EscapeDataString(id)}?uploadType=media", Json(json), ct);
                return;
            }
            catch (CloudException ex) when (ex.Status == 404)
            {
                _ids.Remove(name); // deleted by another device ("delete cloud data"): create it again
            }
        }

        var meta = new JsonObject { ["name"] = name, ["parents"] = new JsonArray("appDataFolder") };
        var body = new MultipartContent("related") { Json(meta.ToJsonString()), Json(json) };
        var created = ParseAnswer(await Send(HttpMethod.Post, $"{UploadApi}?uploadType=multipart", body, ct));
        _ids[name] = (string)created["id"]!;
    }

    public async Task Delete(CloudFile f, CancellationToken ct)
    {
        await Send(HttpMethod.Delete, $"{Api}/{Uri.EscapeDataString(f.Id)}", null, ct);
        _ids?.Remove(f.Name);
    }

    static StringContent Json(string text) => new(text, Encoding.UTF8, "application/json");

    /// <summary>Drive's answer as JSON. Anything else (e.g. a hotel Wi-Fi's login page) counts as no connection.</summary>
    static JsonNode ParseAnswer(string text)
    {
        try { return JsonNode.Parse(text) ?? throw new JsonException("Empty answer."); }
        catch (JsonException) { throw new CloudException(CloudError.Offline, "Google Drive's answer wasn't readable."); }
    }

    async Task<string> Send(HttpMethod method, string url, HttpContent? content, CancellationToken ct)
    {
        var token = await accessToken(ct) ?? throw new CloudException(CloudError.Unauthorized, "Not signed in.");
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct); }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new CloudException(CloudError.Offline, "Can't reach Google Drive: " + ex.GetType().Name);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode) return text;
            var code = (int)response.StatusCode;
            var kind = code switch
            {
                401 => CloudError.Unauthorized,
                429 => CloudError.RateLimited,
                403 when text.Contains("RateLimitExceeded", StringComparison.OrdinalIgnoreCase) => CloudError.RateLimited,
                // the user unticked Drive access on Google's consent page: only signing in again helps
                403 when text.Contains("insufficient", StringComparison.OrdinalIgnoreCase) => CloudError.Unauthorized,
                >= 500 => CloudError.Server,
                _ => CloudError.Other,
            };
            throw new CloudException(kind, $"HTTP {code} from Google Drive") { Status = code };
        }
    }
}
