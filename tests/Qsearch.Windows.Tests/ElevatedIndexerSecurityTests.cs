using System.Reflection;
using System.Runtime.CompilerServices;
using Qsearch.ElevatedIndexer;
using Qsearch.Windows.Elevation;
using Qsearch.Windows.Usn;

namespace Qsearch.Windows.Tests;

public sealed class ElevatedIndexerSecurityTests
{
    [Theory]
    [InlineData(@"C:\Users\QA3\secret.txt")]
    [InlineData("cmd.exe")]
    public async Task Helper_RejectsArbitraryPathOrProcessCommands(string payload)
    {
        HelperCommandDispatcher dispatcher = CreateDispatcher();
        await AuthenticateAsync(dispatcher);
        ElevatedIndexerMessage request = ElevatedIndexerProtocol.Create(
            "OpenPath",
            new { Payload = payload });

        HelperResponse response = await dispatcher.DispatchAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal("unsupported-command", response.ErrorCode);
        Assert.Equal(0, dispatcher.VolumeReaderOpenCount);
    }

    [Fact]
    public async Task Helper_RejectsWrongNonceBeforeVolumeAccess()
    {
        HelperCommandDispatcher dispatcher = CreateDispatcher();
        ElevatedIndexerMessage hello = ElevatedIndexerProtocol.Create(
            ElevatedIndexerMessageTypes.Hello,
            new HelloMessage(CurrentSid, "bad"));

        HelperResponse response = await dispatcher.DispatchAsync(
            hello,
            TestContext.Current.CancellationToken);

        Assert.Equal("authentication-failed", response.ErrorCode);
        Assert.Equal(0, dispatcher.VolumeReaderOpenCount);
    }

    [Fact]
    public async Task Helper_RejectsWrongSidReplayAndUnconsentedVolume()
    {
        HelperCommandDispatcher wrongSid = CreateDispatcher();
        HelperResponse sidResponse = await wrongSid.DispatchAsync(
            ElevatedIndexerProtocol.Create(
                ElevatedIndexerMessageTypes.Hello,
                new HelloMessage("S-1-5-21-other", GoodNonce)),
            TestContext.Current.CancellationToken);
        Assert.Equal("authentication-failed", sidResponse.ErrorCode);

        HelperCommandDispatcher dispatcher = CreateDispatcher();
        await AuthenticateAsync(dispatcher);
        HelperResponse replay = await dispatcher.DispatchAsync(
            ElevatedIndexerProtocol.Create(
                ElevatedIndexerMessageTypes.Hello,
                new HelloMessage(CurrentSid, GoodNonce)),
            TestContext.Current.CancellationToken);
        Assert.Equal("replayed-hello", replay.ErrorCode);

        HelperResponse volume = await dispatcher.DispatchAsync(
            ElevatedIndexerProtocol.Create(
                ElevatedIndexerMessageTypes.EnumerateVolume,
                new EnumerateVolumeMessage("D:\\")),
            TestContext.Current.CancellationToken);
        Assert.Equal("volume-not-consented", volume.ErrorCode);
        Assert.Equal(0, dispatcher.VolumeReaderOpenCount);
    }

    [Fact]
    public async Task Helper_CancellationStopsBeforeVolumeAccess()
    {
        HelperCommandDispatcher dispatcher = CreateDispatcher();
        await AuthenticateAsync(dispatcher);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => dispatcher.DispatchAsync(
                ElevatedIndexerProtocol.Create(
                    ElevatedIndexerMessageTypes.EnumerateVolume,
                    new EnumerateVolumeMessage("C:\\")),
                cancellation.Token).AsTask());

        Assert.Equal(0, dispatcher.VolumeReaderOpenCount);
    }

    [Fact]
    public async Task Helper_QueryJournal_ReturnsStateOnlyForConsentedVolume()
    {
        HelperCommandDispatcher dispatcher = CreateDispatcher(
            reader: new SequenceUsnReader([]));
        await AuthenticateAsync(dispatcher);

        HelperResponse allowed = await dispatcher.DispatchAsync(
            ElevatedIndexerProtocol.Create(
                ElevatedIndexerMessageTypes.QueryJournal,
                new QueryJournalMessage("C:\\")),
            TestContext.Current.CancellationToken);
        HelperResponse rejected = await dispatcher.DispatchAsync(
            ElevatedIndexerProtocol.Create(
                ElevatedIndexerMessageTypes.QueryJournal,
                new QueryJournalMessage("D:\\")),
            TestContext.Current.CancellationToken);

        Assert.True(allowed.Success);
        Assert.Equal(new UsnJournalState(1, 0, 1), allowed.Journal);
        Assert.Equal("volume-not-consented", rejected.ErrorCode);
        Assert.Equal(1, dispatcher.VolumeReaderOpenCount);
    }

    [Fact]
    public void HelperReferenceGraph_HasNoProcessLaunchAssembly()
    {
        Assembly helperAssembly = typeof(HelperCommandDispatcher).Assembly;

        Assert.DoesNotContain(
            helperAssembly.GetReferencedAssemblies(),
            reference => reference.Name == "System.Diagnostics.Process");
        Assert.DoesNotContain(
            helperAssembly.GetExportedTypes().SelectMany(type => type.GetMethods()),
            method => method.Name is "Delete" or "Move" or "Start");
    }

    [Fact]
    public void IdleSession_ExpiresWithoutOpeningVolume()
    {
        FakeTimeProvider time = new();
        HelperCommandDispatcher dispatcher = CreateDispatcher(time);
        time.Advance(TimeSpan.FromSeconds(31));

        Assert.True(dispatcher.IsIdleExpired(TimeSpan.FromSeconds(30)));
        Assert.Equal(0, dispatcher.VolumeReaderOpenCount);
    }

    [Fact]
    public async Task Helper_EnumeratesMoreThanBatchLimitWithoutLossOrDuplicates()
    {
        UsnRecord[] records = Enumerable.Range(1, 4501)
            .Select(CreateRecord)
            .ToArray();
        HelperCommandDispatcher dispatcher = CreateDispatcher(
            reader: new SequenceUsnReader(records));
        await AuthenticateAsync(dispatcher);

        HelperResponse first = await EnumerateAsync(dispatcher, continuationToken: null);
        HelperResponse second = await EnumerateAsync(dispatcher, first.ContinuationToken);
        HelperResponse third = await EnumerateAsync(dispatcher, second.ContinuationToken);

        Assert.Equal(2000, first.Records!.Count);
        Assert.False(first.Completed);
        Assert.NotNull(first.ContinuationToken);
        Assert.Equal(2000, second.Records!.Count);
        Assert.False(second.Completed);
        Assert.NotNull(second.ContinuationToken);
        Assert.Equal(501, third.Records!.Count);
        Assert.True(third.Completed);
        Assert.Null(third.ContinuationToken);
        string[] identities = first.Records.Concat(second.Records).Concat(third.Records)
            .Select(record => record.FileReference)
            .ToArray();
        Assert.Equal(records.Select(record => record.FileReferenceNumber.ToString()), identities);
        Assert.Equal(identities.Length, identities.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Helper_RejectsReplayedMalformedAndCrossVolumeContinuation()
    {
        HelperCommandDispatcher dispatcher = new(
            CurrentSid,
            GoodNonce,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C:\\", "D:\\" },
            new SequenceUsnReader(Enumerable.Range(1, 2500).Select(CreateRecord).ToArray()),
            TimeProvider.System);
        await AuthenticateAsync(dispatcher);
        HelperResponse first = await EnumerateAsync(dispatcher, continuationToken: null);

        HelperResponse malformed = await EnumerateAsync(dispatcher, "not-a-cursor");
        Assert.Equal("invalid-continuation", malformed.ErrorCode);

        HelperResponse crossVolume = await dispatcher.DispatchAsync(
            ElevatedIndexerProtocol.Create(
                ElevatedIndexerMessageTypes.EnumerateVolume,
                new EnumerateVolumeMessage("D:\\", first.ContinuationToken)),
            TestContext.Current.CancellationToken);
        Assert.Equal("invalid-continuation", crossVolume.ErrorCode);

        HelperResponse second = await EnumerateAsync(dispatcher, first.ContinuationToken);
        Assert.True(second.Completed);
        HelperResponse replay = await EnumerateAsync(dispatcher, first.ContinuationToken);
        Assert.Equal("invalid-continuation", replay.ErrorCode);
    }

    private const string CurrentSid = "S-1-5-21-current";
    private const string GoodNonce = "good";

    private static HelperCommandDispatcher CreateDispatcher(
        TimeProvider? timeProvider = null,
        IUsnVolumeReader? reader = null)
    {
        return new HelperCommandDispatcher(
            CurrentSid,
            GoodNonce,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C:\\" },
            reader ?? new EmptyUsnReader(),
            timeProvider ?? TimeProvider.System);
    }

    private static async ValueTask<HelperResponse> EnumerateAsync(
        HelperCommandDispatcher dispatcher,
        string? continuationToken)
    {
        return await dispatcher.DispatchAsync(
            ElevatedIndexerProtocol.Create(
                ElevatedIndexerMessageTypes.EnumerateVolume,
                new EnumerateVolumeMessage("C:\\", continuationToken)),
            TestContext.Current.CancellationToken);
    }

    private static UsnRecord CreateRecord(int position)
    {
        return new UsnRecord(
            new FileReferenceId((ulong)position),
            new FileReferenceId(5),
            position,
            UsnReason.FileCreate,
            FileAttributes.Normal,
            $"file-{position:D5}.txt",
            2);
    }

    private static async ValueTask AuthenticateAsync(HelperCommandDispatcher dispatcher)
    {
        HelperResponse response = await dispatcher.DispatchAsync(
            ElevatedIndexerProtocol.Create(
                ElevatedIndexerMessageTypes.Hello,
                new HelloMessage(CurrentSid, GoodNonce)),
            TestContext.Current.CancellationToken);
        Assert.True(response.Success);
    }

    private sealed class EmptyUsnReader : IUsnVolumeReader
    {
        public ValueTask<UsnJournalState> QueryJournalAsync(
            string volumeRoot,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new UsnJournalState(1, 0, 1));
        }

        public async IAsyncEnumerable<UsnRecord> EnumerateAsync(
            string volumeRoot,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<UsnReadBatch> ReadChangesAsync(
            string volumeRoot,
            long startUsn,
            long journalId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class SequenceUsnReader(IReadOnlyList<UsnRecord> records) : IUsnVolumeReader
    {
        public ValueTask<UsnJournalState> QueryJournalAsync(
            string volumeRoot,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new UsnJournalState(1, 0, records.Count + 1));

        public async IAsyncEnumerable<UsnRecord> EnumerateAsync(
            string volumeRoot,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (UsnRecord record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return record;
            }

            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<UsnReadBatch> ReadChangesAsync(
            string volumeRoot,
            long startUsn,
            long journalId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
