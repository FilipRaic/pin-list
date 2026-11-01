using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace PinList;

/// <summary>
/// Makes sure only one PinList runs. A second start signals the first one (which then brings its widgets
/// to the front) and quits. The check is a named mutex; the signal goes through a named pipe, which works on
/// every platform.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    readonly Mutex _mutex;
    readonly string _pipeName;
    readonly CancellationTokenSource _stop = new();
    NamedPipeServerStream? _server;

    /// <summary>True if no other PinList with this name was running.</summary>
    public bool IsFirst { get; }

    /// <param name="name">Unique per data folder, so a development copy can run next to the normal one.</param>
    public SingleInstance(string name)
    {
        _mutex = new Mutex(true, OperatingSystem.IsWindows() ? $@"Local\{name}.SingleInstance" : $"{name}.SingleInstance", out bool createdNew);
        IsFirst = createdNew;
        _pipeName = $"{name}.ShowWidgets";
    }

    /// <summary>Calls <paramref name="onShow"/> (on a background thread) whenever another start signals this one.</summary>
    public void ListenForShow(Action onShow)
    {
        var token = _stop.Token;
        // the first pipe exists before this returns, so a start right afterwards can't miss it
        var server = _server = NewServer();
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (server)
                    {
                        try { await server.WaitForConnectionAsync(token); }
                        // the other start connected and closed its end before this side finished accepting
                        // ("the pipe is being closed"): it still asked to show the widgets
                        catch (System.IO.IOException) { }
                        onShow();
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Store.Log("Single instance: " + ex.Message);
                    await Task.Delay(1000, token).ContinueWith(_ => { });
                }
                if (!token.IsCancellationRequested) server = _server = NewServer();
            }
        });
    }

    NamedPipeServerStream NewServer() =>
        new(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    /// <summary>Tells the running instance to show its widgets. Returns false if none is listening.</summary>
    public bool SignalFirst()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            // Windows only lets a process the user just started take the foreground; pass that right on
            Native.AllowAnyForeground();
            client.Connect(1000);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or System.IO.IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _server?.Dispose(); // right away, so the name is free for the next start
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { } // released by another thread than the one that took it; disposing frees it anyway
        }
        _mutex.Dispose();
    }
}
