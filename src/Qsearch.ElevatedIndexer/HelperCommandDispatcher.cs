using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Qsearch.Windows.Elevation;
using Qsearch.Windows.Usn;

namespace Qsearch.ElevatedIndexer;

public sealed class HelperCommandDispatcher
{
    private sealed class EnumerationSession(IAsyncEnumerator<UsnRecord> enumerator)
    {
        public IAsyncEnumerator<UsnRecord> Enumerator { get; } = enumerator;

        public UsnRecord? Buffered { get; set; }

        public int Page { get; set; }

        public string? ExpectedToken { get; set; }
    }

    private sealed record CursorPayload(string VolumeId, int Page, string LastReference);

    private const int MaximumBatchSize = 2000;
    private readonly string _expectedUserSid;
    private readonly string _expectedNonce;
    private readonly HashSet<string> _allowedVolumes;
    private readonly IUsnVolumeReader _volumeReader;
    private readonly TimeProvider _timeProvider;
    private readonly byte[] _cursorKey;
    private readonly Dictionary<string, EnumerationSession> _enumerations =
        new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastActivity;
    private bool _authenticated;
    private bool _helloConsumed;

    public HelperCommandDispatcher(
        string expectedUserSid,
        string expectedNonce,
        IReadOnlySet<string> allowedVolumes,
        IUsnVolumeReader volumeReader,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedUserSid);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedNonce);
        ArgumentNullException.ThrowIfNull(allowedVolumes);
        ArgumentNullException.ThrowIfNull(volumeReader);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _expectedUserSid = expectedUserSid;
        _expectedNonce = expectedNonce;
        _allowedVolumes = new HashSet<string>(allowedVolumes, StringComparer.OrdinalIgnoreCase);
        _volumeReader = volumeReader;
        _timeProvider = timeProvider;
        _cursorKey = SHA256.HashData(Encoding.UTF8.GetBytes(expectedNonce));
        _lastActivity = timeProvider.GetUtcNow();
    }

    public int VolumeReaderOpenCount { get; private set; }

    public bool ShouldExit { get; private set; }

    public bool IsIdleExpired(TimeSpan idleTimeout)
    {
        return _timeProvider.GetUtcNow() - _lastActivity >= idleTimeout;
    }

    public async ValueTask<HelperResponse> DispatchAsync(
        ElevatedIndexerMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        _lastActivity = _timeProvider.GetUtcNow();

        if (message.Type == ElevatedIndexerMessageTypes.Hello)
        {
            return Authenticate(message);
        }

        if (!_authenticated)
        {
            return HelperResponse.Error("authentication-required");
        }

        try
        {
            return message.Type switch
            {
                ElevatedIndexerMessageTypes.QueryJournal =>
                    await QueryJournalAsync(message, cancellationToken),
                ElevatedIndexerMessageTypes.EnumerateVolume =>
                    await EnumerateAsync(message, cancellationToken),
                ElevatedIndexerMessageTypes.ReadJournal =>
                    await ReadJournalAsync(message, cancellationToken),
                ElevatedIndexerMessageTypes.Cancel => Cancel(message),
                ElevatedIndexerMessageTypes.Goodbye => Goodbye(),
                _ => HelperResponse.Error("unsupported-command"),
            };
        }
        catch (JsonException)
        {
            return HelperResponse.Error("malformed-message");
        }
        catch (InvalidDataException)
        {
            return HelperResponse.Error("malformed-message");
        }
    }

    private async ValueTask<HelperResponse> QueryJournalAsync(
        ElevatedIndexerMessage message,
        CancellationToken cancellationToken)
    {
        QueryJournalMessage request = message.Deserialize<QueryJournalMessage>();
        if (!IsAllowedVolume(request.VolumeId))
        {
            return HelperResponse.Error("volume-not-consented");
        }

        cancellationToken.ThrowIfCancellationRequested();
        VolumeReaderOpenCount++;
        UsnJournalState journal = await _volumeReader.QueryJournalAsync(
            request.VolumeId,
            cancellationToken);
        return HelperResponse.Ok(journal: journal);
    }

    private HelperResponse Authenticate(ElevatedIndexerMessage message)
    {
        if (_helloConsumed)
        {
            return HelperResponse.Error("replayed-hello");
        }

        _helloConsumed = true;
        HelloMessage hello;
        try
        {
            hello = message.Deserialize<HelloMessage>();
        }
        catch (JsonException)
        {
            return HelperResponse.Error("authentication-failed");
        }

        if (!string.Equals(hello.UserSid, _expectedUserSid, StringComparison.Ordinal) ||
            !FixedTimeEquals(hello.Nonce, _expectedNonce))
        {
            return HelperResponse.Error("authentication-failed");
        }

        _authenticated = true;
        return HelperResponse.Ok();
    }

    private async ValueTask<HelperResponse> EnumerateAsync(
        ElevatedIndexerMessage message,
        CancellationToken cancellationToken)
    {
        EnumerateVolumeMessage request = message.Deserialize<EnumerateVolumeMessage>();
        if (!IsAllowedVolume(request.VolumeId))
        {
            return HelperResponse.Error("volume-not-consented");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!_enumerations.TryGetValue(request.VolumeId, out EnumerationSession? session))
        {
            if (request.ContinuationToken is not null)
            {
                return HelperResponse.Error("invalid-continuation");
            }

            VolumeReaderOpenCount++;
            session = new EnumerationSession(
                _volumeReader
                    .EnumerateAsync(request.VolumeId, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken));
            _enumerations.Add(request.VolumeId, session);
        }
        else if (request.ContinuationToken is null ||
                 !ValidateCursor(request.VolumeId, request.ContinuationToken, session))
        {
            return HelperResponse.Error("invalid-continuation");
        }

        List<UsnRecordMessage> records = new(MaximumBatchSize);
        if (session.Buffered is not null)
        {
            records.Add(UsnRecordMessage.FromRecord(session.Buffered));
            session.Buffered = null;
        }

        while (records.Count < MaximumBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await session.Enumerator.MoveNextAsync())
            {
                await CompleteEnumerationAsync(request.VolumeId, session);
                return HelperResponse.Ok(records, completed: true);
            }

            records.Add(UsnRecordMessage.FromRecord(session.Enumerator.Current));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!await session.Enumerator.MoveNextAsync())
        {
            await CompleteEnumerationAsync(request.VolumeId, session);
            return HelperResponse.Ok(records, completed: true);
        }

        session.Buffered = session.Enumerator.Current;
        session.Page++;
        string continuationToken = CreateCursor(
            request.VolumeId,
            session.Page,
            records[^1].FileReference);
        session.ExpectedToken = continuationToken;
        return HelperResponse.Ok(
            records,
            continuationToken: continuationToken,
            completed: false);
    }

    private async ValueTask CompleteEnumerationAsync(
        string volumeId,
        EnumerationSession session)
    {
        _enumerations.Remove(volumeId);
        await session.Enumerator.DisposeAsync();
    }

    private bool ValidateCursor(
        string volumeId,
        string token,
        EnumerationSession session)
    {
        if (!string.Equals(token, session.ExpectedToken, StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = token.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        try
        {
            byte[] payload = DecodeBase64Url(parts[0]);
            byte[] signature = DecodeBase64Url(parts[1]);
            byte[] expected = HMACSHA256.HashData(_cursorKey, payload);
            if (signature.Length != expected.Length ||
                !CryptographicOperations.FixedTimeEquals(signature, expected))
            {
                return false;
            }

            CursorPayload? cursor = JsonSerializer.Deserialize<CursorPayload>(payload);
            return cursor is not null &&
                string.Equals(cursor.VolumeId, volumeId, StringComparison.OrdinalIgnoreCase) &&
                cursor.Page == session.Page &&
                !string.IsNullOrWhiteSpace(cursor.LastReference);
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private string CreateCursor(string volumeId, int page, string lastReference)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new CursorPayload(volumeId, page, lastReference));
        byte[] signature = HMACSHA256.HashData(_cursorKey, payload);
        return EncodeBase64Url(payload) + "." + EncodeBase64Url(signature);
    }

    private static string EncodeBase64Url(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] DecodeBase64Url(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        return Convert.FromBase64String(base64);
    }

    private async ValueTask<HelperResponse> ReadJournalAsync(
        ElevatedIndexerMessage message,
        CancellationToken cancellationToken)
    {
        ReadJournalMessage request = message.Deserialize<ReadJournalMessage>();
        if (!IsAllowedVolume(request.VolumeId))
        {
            return HelperResponse.Error("volume-not-consented");
        }

        cancellationToken.ThrowIfCancellationRequested();
        VolumeReaderOpenCount++;
        await using IAsyncEnumerator<UsnReadBatch> enumerator = _volumeReader
            .ReadChangesAsync(
                request.VolumeId,
                request.StartUsn,
                request.JournalId,
                cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        if (!await enumerator.MoveNextAsync())
        {
            return HelperResponse.Ok([], nextUsn: request.StartUsn);
        }

        UsnReadBatch batch = enumerator.Current;
        IReadOnlyList<UsnRecordMessage> records = batch.Records
            .Take(MaximumBatchSize)
            .Select(UsnRecordMessage.FromRecord)
            .ToArray();
        return HelperResponse.Ok(records, nextUsn: batch.NextUsn);
    }

    private static HelperResponse Cancel(ElevatedIndexerMessage message)
    {
        _ = message.Deserialize<CancelMessage>();
        return HelperResponse.Ok();
    }

    private HelperResponse Goodbye()
    {
        ShouldExit = true;
        return HelperResponse.Ok();
    }

    private bool IsAllowedVolume(string volumeId)
    {
        return !string.IsNullOrWhiteSpace(volumeId) && _allowedVolumes.Contains(volumeId);
    }

    private static bool FixedTimeEquals(string actual, string expected)
    {
        byte[] actualBytes = Encoding.UTF8.GetBytes(actual);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
