using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PinList.Sync;

/// <summary>
/// The desktop sign-in redirect: a tiny web server on 127.0.0.1 (random port) that receives Google's answer after
/// the user signed in in their browser (RFC 8252 loopback redirect).
/// </summary>
public static class LoopbackSignIn
{
    const string SignedIn = "You're signed in to PinList. You can close this tab.";
    const string Cancelled = "Sign-in was cancelled.";

    /// <summary>
    /// Opens the browser at <paramref name="authorizeUrlFor"/>(redirect URI) and waits for the redirect. Returns the
    /// authorization code, or null when the user said no, the state doesn't match or <paramref name="ct"/> was cancelled.
    /// </summary>
    public static async Task<string?> WaitForCode(Func<string, Task> openBrowser, Func<string, string> authorizeUrlFor,
        string expectedState, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            await openBrowser(authorizeUrlFor(redirect));
            // Browsers open extra connections (preconnect, favicon): each is served on its own, so one that never
            // sends anything can't hold up the real answer
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var client = await listener.AcceptTcpClientAsync(stop.Token);
                        _ = Serve(client, expectedState, result, stop.Token);
                    }
                }
                catch (Exception) { result.TrySetResult(null); } // stopped (cancelled, or the listener closed)
            });
            using (ct.Register(() => result.TrySetResult(null)))
                return await result.Task;
        }
        finally
        {
            stop.Cancel();
            listener.Stop();
        }
    }

    /// <summary>One connection: reads its request (for at most a few seconds) and answers it.</summary>
    static async Task Serve(TcpClient client, string expectedState, TaskCompletionSource<string?> result, CancellationToken stop)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var stream = client.GetStream();
                var query = Query(await ReadRequestTarget(stream, timeout.Token));
                if (query.code == null && query.error == null)
                {
                    await Answer(stream, 404, "", timeout.Token); // the browser asking for /favicon.ico
                    return;
                }
                bool ok = query.code != null && query.state == expectedState;
                await Answer(stream, 200, ok ? SignedIn : Cancelled, timeout.Token);
                result.TrySetResult(ok ? query.code : null);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
        }
    }

    /// <summary>The path and query of the request line ("GET /?code=… HTTP/1.1").</summary>
    static async Task<string> ReadRequestTarget(Stream stream, CancellationToken ct)
    {
        var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        var line = await reader.ReadLineAsync(ct) ?? "";
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(ct))) { } // headers
        var parts = line.Split(' ');
        return parts.Length > 1 ? parts[1] : "";
    }

    static (string? code, string? state, string? error) Query(string target)
    {
        int q = target.IndexOf('?');
        var pairs = new Dictionary<string, string>();
        foreach (var p in (q < 0 ? "" : target[(q + 1)..]).Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)))
            pairs.TryAdd(p[0], Uri.UnescapeDataString(p.Length > 1 ? p[1] : "")); // a repeated name: the first one counts
        return (pairs.GetValueOrDefault("code"), pairs.GetValueOrDefault("state"), pairs.GetValueOrDefault("error"));
    }

    static async Task Answer(Stream stream, int status, string message, CancellationToken ct)
    {
        var html = message.Length == 0 ? "" :
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>PinList</title></head>" +
            $"<body style=\"font-family:sans-serif;text-align:center;margin-top:4em\"><p>{message}</p></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }
}
