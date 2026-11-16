using System.Buffers.Binary;
using System.Text.Json;
using Qsearch.Windows.Usn;

namespace Qsearch.Windows.Elevation;

public static class ElevatedIndexerMessageTypes
{
    public const string Hello = "Hello";
    public const string QueryJournal = "QueryJournal";
    public const string EnumerateVolume = "EnumerateVolume";
    public const string ReadJournal = "ReadJournal";
    public const string Cancel = "Cancel";
    public const string Goodbye = "Goodbye";
}

public sealed record HelloMessage(string UserSid, string Nonce);

public sealed record QueryJournalMessage(string VolumeId);

public sealed record EnumerateVolumeMessage(string VolumeId, string? ContinuationToken = null);

public sealed record ReadJournalMessage(string VolumeId, long StartUsn, long JournalId);

public sealed record CancelMessage(string RequestId);

public sealed record GoodbyeMessage;

public sealed record UsnRecordMessage(
    string FileReference,
    string ParentFileReference,
    long Usn,
    UsnReason Reason,
    FileAttributes Attributes,
    string Name,
    ushort MajorVersion)
{
    public static UsnRecordMessage FromRecord(UsnRecord record)
    {
        return new UsnRecordMessage(
            record.FileReferenceNumber.ToString(),
            record.ParentFileReferenceNumber.ToString(),
            record.Usn,
            record.Reason,
            record.Attributes,
            record.Name,
            record.MajorVersion);
    }

    public UsnRecord ToRecord()
    {
        return new UsnRecord(
            FileReferenceId.Parse(FileReference),
            FileReferenceId.Parse(ParentFileReference),
            Usn,
            Reason,
            Attributes,
            Name,
            MajorVersion);
    }
}

public sealed record HelperResponse(
    bool Success,
    string? ErrorCode = null,
    IReadOnlyList<UsnRecordMessage>? Records = null,
    UsnJournalState? Journal = null,
    long? NextUsn = null,
    string? ContinuationToken = null,
    bool Completed = true)
{
    public static HelperResponse Error(string code) => new(false, code);

    public static HelperResponse Ok(
        IReadOnlyList<UsnRecordMessage>? records = null,
        UsnJournalState? journal = null,
        long? nextUsn = null,
        string? continuationToken = null,
        bool completed = true) => new(
            true,
            Records: records,
            Journal: journal,
            NextUsn: nextUsn,
            ContinuationToken: continuationToken,
            Completed: completed);
}

public sealed record ElevatedIndexerMessage(string Type, JsonElement Payload)
{
    public T Deserialize<T>()
    {
        return Payload.Deserialize<T>(ElevatedIndexerProtocol.SerializerOptions) ??
            throw new InvalidDataException("The helper message payload was empty.");
    }
}

public static class ElevatedIndexerProtocol
{
    public const int MaximumMessageLength = 1024 * 1024;

    internal static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);

    public static ElevatedIndexerMessage Create<T>(string type, T payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return new ElevatedIndexerMessage(
            type,
            JsonSerializer.SerializeToElement(payload, SerializerOptions));
    }

    public static async ValueTask WriteAsync<T>(
        Stream stream,
        T message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
        if (payload.Length is <= 0 or > MaximumMessageLength)
        {
            throw new InvalidDataException("The helper message exceeded the allowed size.");
        }

        byte[] prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async ValueTask<ElevatedIndexerMessage> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] prefix = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(prefix, cancellationToken);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > MaximumMessageLength)
        {
            throw new InvalidDataException("The helper message length was invalid.");
        }

        byte[] payload = GC.AllocateUninitializedArray<byte>(length);
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<ElevatedIndexerMessage>(payload, SerializerOptions) ??
            throw new InvalidDataException("The helper message was empty.");
    }

    public static async ValueTask<HelperResponse> ReadResponseAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] prefix = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(prefix, cancellationToken);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > MaximumMessageLength)
        {
            throw new InvalidDataException("The helper response length was invalid.");
        }

        byte[] payload = GC.AllocateUninitializedArray<byte>(length);
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<HelperResponse>(payload, SerializerOptions) ??
            throw new InvalidDataException("The helper response was empty.");
    }
}
