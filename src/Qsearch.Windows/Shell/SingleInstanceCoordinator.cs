using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Qsearch.Windows.Shell;

public enum ActivationSurface
{
    Default,
    Full,
    Compact,
    Launcher,
}

public sealed record ActivationMessage(
    string? SearchText,
    ActivationSurface Surface = ActivationSurface.Default);

public sealed record InstanceAcquisition(bool IsPrimary, bool Forwarded);

public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    public const int MaximumMessageBytes = 4096;
    private readonly string _mutexName;
    private readonly string _pipeName;
    private readonly Func<ActivationMessage, ValueTask> _onActivation;
    private readonly PipeOptions _pipeOptions;
    private readonly CancellationTokenSource _shutdown = new();
    private Mutex? _mutex;
    private Task? _serverTask;
    private bool _acquired;
    private bool _disposed;

    public SingleInstanceCoordinator(
        string instanceKey,
        Func<ActivationMessage, ValueTask> onActivation,
        bool restrictToCurrentUser = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceKey);
        ArgumentNullException.ThrowIfNull(onActivation);
        string identity = Environment.UserDomainName + "\\" + Environment.UserName + ":" + instanceKey;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        _mutexName = "Local\\Qsearch-" + hash;
        _pipeName = "Qsearch-" + hash;
        _onActivation = onActivation;
        RestrictsToCurrentUser = restrictToCurrentUser;
        _pipeOptions = PipeOptions.Asynchronous |
            (restrictToCurrentUser ? PipeOptions.CurrentUserOnly : PipeOptions.None);
    }

    public bool RestrictsToCurrentUser { get; }

    public async ValueTask<InstanceAcquisition> AcquireOrForwardAsync(
        ActivationMessage message,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(message);
        if (_acquired)
        {
            throw new InvalidOperationException("This coordinator has already acquired an instance role.");
        }

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message);
        if (payload.Length > MaximumMessageBytes)
        {
            throw new ArgumentException("The activation message exceeds the local IPC limit.", nameof(message));
        }

        _acquired = true;
        _mutex = new Mutex(false, _mutexName, out bool createdNew);
        if (createdNew)
        {
            TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _serverTask = RunServerAsync(ready, _shutdown.Token);
            await ready.Task.WaitAsync(cancellationToken);
            return new InstanceAcquisition(true, false);
        }

        await ForwardAsync(payload, cancellationToken);
        return new InstanceAcquisition(false, true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _shutdown.CancelAsync();
        if (_serverTask is not null)
        {
            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _mutex?.Dispose();
        _shutdown.Dispose();
    }

    private async Task RunServerAsync(TaskCompletionSource ready, CancellationToken cancellationToken)
    {
        bool signaled = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using NamedPipeServerStream server = new(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    _pipeOptions);
                if (!signaled)
                {
                    ready.TrySetResult();
                    signaled = true;
                }

                await server.WaitForConnectionAsync(cancellationToken);
                byte[] lengthBytes = new byte[sizeof(int)];
                await ReadExactlyAsync(server, lengthBytes, cancellationToken);
                int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                if (length is <= 0 or > MaximumMessageBytes)
                {
                    continue;
                }

                byte[] payload = new byte[length];
                await ReadExactlyAsync(server, payload, cancellationToken);
                ActivationMessage? message = JsonSerializer.Deserialize<ActivationMessage>(payload);
                if (message is not null)
                {
                    await _onActivation(message);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (!signaled)
            {
                ready.TrySetCanceled(cancellationToken);
            }
        }
    }

    private async ValueTask ForwardAsync(byte[] payload, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await using NamedPipeClientStream client = new(
            ".",
            _pipeName,
            PipeDirection.InOut,
            _pipeOptions);
        await client.ConnectAsync(timeout.Token);
        byte[] lengthBytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, payload.Length);
        await client.WriteAsync(lengthBytes, timeout.Token);
        await client.WriteAsync(payload, timeout.Token);
        await client.FlushAsync(timeout.Token);
    }

    private static async ValueTask ReadExactlyAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = await stream.ReadAsync(destination[offset..], cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }
}
