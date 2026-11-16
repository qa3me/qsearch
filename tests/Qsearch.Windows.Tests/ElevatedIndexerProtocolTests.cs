using System.Buffers.Binary;
using System.Text.Json;
using Qsearch.Windows.Elevation;
using Qsearch.Windows.Usn;

namespace Qsearch.Windows.Tests;

public sealed class ElevatedIndexerProtocolTests
{
    [Fact]
    public void UsnRecordMessage_RoundTrips64And128BitReferences()
    {
        UsnRecord expected = new(
            new FileReferenceId(0x0123456789abcdef, 0xfedcba9876543210),
            new FileReferenceId(5),
            42,
            UsnReason.FileCreate,
            FileAttributes.Normal,
            "report.txt",
            3);

        UsnRecord actual = UsnRecordMessage.FromRecord(expected).ToRecord();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Paging_StreamsEveryResponseAndPassesTheAdvancedCursor()
    {
        List<string?> observedCursors = [];
        Queue<HelperResponse> responses = new(
        [
            HelperResponse.Ok(
                [Record("0001")],
                continuationToken: "cursor-1",
                completed: false),
            HelperResponse.Ok([Record("0002")], completed: true),
        ]);
        List<string> identities = [];

        await foreach (UsnRecordMessage record in ElevatedIndexerPaging.StreamAsync(
                           "C:\\",
                           (cursor, _) =>
                           {
                               observedCursors.Add(cursor);
                               return ValueTask.FromResult(responses.Dequeue());
                           },
                           TestContext.Current.CancellationToken))
        {
            identities.Add(record.FileReference);
        }

        Assert.Equal([null, "cursor-1"], observedCursors);
        Assert.Equal(["0001", "0002"], identities);
    }

    [Fact]
    public async Task Paging_RejectsAResponseThatDoesNotAdvance()
    {
        static ValueTask<HelperResponse> Send(string? cursor, CancellationToken _) =>
            ValueTask.FromResult(
                HelperResponse.Ok(
                    [],
                    continuationToken: cursor ?? "same",
                    completed: false));

        await Assert.ThrowsAsync<InvalidDataException>(
            async () =>
            {
                await foreach (UsnRecordMessage _ in ElevatedIndexerPaging.StreamAsync(
                                   "C:\\",
                                   Send,
                                   TestContext.Current.CancellationToken))
                {
                }
            });
    }

    [Fact]
    public async Task LengthPrefixedMessage_RoundTripsWithinLimit()
    {
        ElevatedIndexerMessage message = ElevatedIndexerProtocol.Create(
            ElevatedIndexerMessageTypes.Hello,
            new HelloMessage("S-1-5-21-test", "nonce"));
        await using MemoryStream stream = new();

        await ElevatedIndexerProtocol.WriteAsync(
            stream,
            message,
            TestContext.Current.CancellationToken);
        stream.Position = 0;
        ElevatedIndexerMessage restored = await ElevatedIndexerProtocol.ReadAsync(
            stream,
            TestContext.Current.CancellationToken);

        Assert.Equal(ElevatedIndexerMessageTypes.Hello, restored.Type);
        Assert.Equal("nonce", restored.Deserialize<HelloMessage>().Nonce);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ElevatedIndexerProtocol.MaximumMessageLength + 1)]
    public async Task MalformedLength_IsRejectedBeforeAllocation(int length)
    {
        byte[] prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, length);
        await using MemoryStream stream = new(prefix);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ElevatedIndexerProtocol.ReadAsync(
                stream,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task TruncatedPayload_IsRejected()
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), 100);
        await using MemoryStream stream = new(bytes);

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => ElevatedIndexerProtocol.ReadAsync(
                stream,
                TestContext.Current.CancellationToken).AsTask());
    }

    private static UsnRecordMessage Record(string identity)
    {
        return new UsnRecordMessage(
            identity,
            "0005",
            1,
            UsnReason.FileCreate,
            FileAttributes.Normal,
            identity + ".txt",
            2);
    }
}
