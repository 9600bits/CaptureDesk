using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace CaptureDesk.App;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    internal const string MutexName = @"Global\CaptureDesk.SingleInstance";
    private const string PipeName = "CaptureDesk.SingleInstance";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private readonly Action<string[]> _activate;
    private readonly Task _listener;
    private bool _disposed;

    private SingleInstanceCoordinator(Mutex mutex, Action<string[]> activate)
    {
        _mutex = mutex;
        _activate = activate;
        _listener = ListenAsync();
    }

    public static SingleInstanceCoordinator? TryAcquire(string[] arguments, Action<string[]> activate, out bool notified)
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew)
        {
            notified = false;
            return new SingleInstanceCoordinator(mutex, activate);
        }

        mutex.Dispose();
        notified = NotifyExistingInstance(arguments);
        return null;
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var reader = new StreamReader(pipe);
                var json = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false);
                var arguments = json is null ? [] : JsonSerializer.Deserialize<string[]>(json) ?? [];
                _activate(arguments);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (IOException) when (!_stop.IsCancellationRequested) { }
            catch (JsonException) { }
        }
    }

    private static bool NotifyExistingInstance(string[] arguments)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                pipe.Connect(200);
                using var writer = new StreamWriter(pipe) { AutoFlush = true };
                writer.WriteLine(JsonSerializer.Serialize(arguments));
                return true;
            }
            catch (TimeoutException) { }
            catch (IOException) { }
            Thread.Sleep(50);
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        try { _listener.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        _stop.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
