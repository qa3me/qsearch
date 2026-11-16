using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Qsearch.Windows.Usn;

namespace Qsearch.Windows.Elevation;

public sealed record VolumeConsent(string UserSid, IReadOnlySet<string> AllowedVolumes);

public interface IElevatedIndexerClient
{
    ValueTask<IElevatedIndexerSession> ConnectAsync(
        VolumeConsent consent,
        CancellationToken cancellationToken);
}

public interface IElevatedIndexerSession : IUsnVolumeReader, IAsyncDisposable;

public sealed class ElevatedIndexerClient : IElevatedIndexerSession
{
    private readonly NamedPipeClientStream _pipe;
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    internal ElevatedIndexerClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
    }

    public async ValueTask<HelperResponse> SendAsync<T>(
        string messageType,
        T payload,
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            ElevatedIndexerMessage message = ElevatedIndexerProtocol.Create(messageType, payload);
            await ElevatedIndexerProtocol.WriteAsync(_pipe, message, cancellationToken);
            return await ElevatedIndexerProtocol.ReadResponseAsync(_pipe, cancellationToken);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async IAsyncEnumerable<UsnRecordMessage> EnumerateVolumeAsync(
        string volumeId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (UsnRecordMessage record in ElevatedIndexerPaging.StreamAsync(
                           volumeId,
                           (continuationToken, token) => SendAsync(
                               ElevatedIndexerMessageTypes.EnumerateVolume,
                               new EnumerateVolumeMessage(volumeId, continuationToken),
                               token),
                           cancellationToken))
        {
            yield return record;
        }
    }

    public async ValueTask<UsnJournalState> QueryJournalAsync(
        string volumeRoot,
        CancellationToken cancellationToken)
    {
        HelperResponse response = await SendAsync(
            ElevatedIndexerMessageTypes.QueryJournal,
            new QueryJournalMessage(volumeRoot),
            cancellationToken);
        if (!response.Success || response.Journal is null)
        {
            throw new InvalidOperationException(
                $"The elevated helper could not query the journal ({response.ErrorCode ?? "missing-state"}).");
        }

        return response.Journal;
    }

    public async IAsyncEnumerable<UsnRecord> EnumerateAsync(
        string volumeRoot,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (UsnRecordMessage record in EnumerateVolumeAsync(
                           volumeRoot,
                           cancellationToken))
        {
            yield return record.ToRecord();
        }
    }

    public async IAsyncEnumerable<UsnReadBatch> ReadChangesAsync(
        string volumeRoot,
        long startUsn,
        long journalId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long nextUsn = startUsn;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HelperResponse response = await SendAsync(
                ElevatedIndexerMessageTypes.ReadJournal,
                new ReadJournalMessage(volumeRoot, nextUsn, journalId),
                cancellationToken);
            if (!response.Success || response.NextUsn is null)
            {
                throw new InvalidOperationException(
                    $"The elevated helper could not read journal changes ({response.ErrorCode ?? "missing-cursor"}).");
            }

            UsnRecord[] records = (response.Records ?? [])
                .Select(record => record.ToRecord())
                .ToArray();
            nextUsn = response.NextUsn.Value;
            yield return new UsnReadBatch(records, nextUsn, []);
            if (records.Length == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_pipe.IsConnected)
        {
            try
            {
                await SendAsync(
                    ElevatedIndexerMessageTypes.Goodbye,
                    new GoodbyeMessage(),
                    CancellationToken.None);
            }
            catch (IOException)
            {
            }
        }

        await _pipe.DisposeAsync();
        _requestGate.Dispose();
    }
}

public static class ElevatedIndexerPaging
{
    public static async IAsyncEnumerable<UsnRecordMessage> StreamAsync(
        string volumeId,
        Func<string?, CancellationToken, ValueTask<HelperResponse>> sendPage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeId);
        ArgumentNullException.ThrowIfNull(sendPage);
        string? continuationToken = null;
        while (true)
        {
            HelperResponse response = await sendPage(continuationToken, cancellationToken);
            if (!response.Success)
            {
                throw new InvalidOperationException(
                    $"The elevated helper rejected volume enumeration ({response.ErrorCode}).");
            }

            foreach (UsnRecordMessage record in response.Records ?? [])
            {
                yield return record;
            }

            if (response.Completed)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(response.ContinuationToken) ||
                string.Equals(
                    continuationToken,
                    response.ContinuationToken,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The elevated helper did not advance the enumeration cursor.");
            }

            continuationToken = response.ContinuationToken;
        }
    }
}
