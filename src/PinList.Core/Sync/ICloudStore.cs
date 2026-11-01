using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PinList.Sync;

/// <summary>A file in PinList's hidden app folder in the user's Google Drive.</summary>
public sealed record CloudFile(string Id, string Name, DateTime ModifiedUtc);

/// <summary>
/// The files in PinList's cloud folder (Drive appDataFolder). Each device writes only its own files, so there are no
/// write races. Another provider could implement this later.
/// </summary>
public interface ICloudStore
{
    Task<IReadOnlyList<CloudFile>> List(CancellationToken ct);
    Task<string> Download(CloudFile f, CancellationToken ct);

    /// <summary>Creates the file with that name, or replaces its content if it exists.</summary>
    Task Upload(string name, string json, CancellationToken ct);

    Task Delete(CloudFile f, CancellationToken ct);
}

public enum CloudError { Offline, Unauthorized, RateLimited, Server, Other }

/// <summary>A cloud call failed. The message never contains tokens or file content.</summary>
public sealed class CloudException(CloudError kind, string message) : Exception(message)
{
    public CloudError Kind { get; } = kind;

    /// <summary>The HTTP status code, or 0 when there was no answer.</summary>
    public int Status { get; init; }
}
