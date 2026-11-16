using Qsearch.App.Services;
using Qsearch.Safety;
using Qsearch.Safety.Evidence;
using Qsearch.Safety.Policy;

namespace Qsearch.App.Tests;

public sealed class LocalFileReviewServiceTests
{
    [Fact]
    public async Task ReviewEngine_RunsAwayFromTheCallingThread()
    {
        ThreadRecordingEvidenceReader reader = new();
        FileReviewEngine engine = new(
            reader,
            new StubAuthenticodeVerifier(),
            ReviewPolicy.VersionOne,
            TimeProvider.System);
        LocalFileReviewService service = new(engine);

        int callingThread = 0;
        TaskCompletionSource<Task> dispatched = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim releaseCaller = new(initialState: false);
        Thread caller = new(() =>
        {
            callingThread = Environment.CurrentManagedThreadId;
            try
            {
                dispatched.TrySetResult(service.ReviewAsync(
                    @"C:\scope\notes.txt",
                    TestContext.Current.CancellationToken).AsTask());
                releaseCaller.Wait(TimeSpan.FromSeconds(30));
            }
            catch (Exception exception)
            {
                dispatched.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
        };

        caller.Start();
        try
        {
            Task review = await dispatched.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            await review;

            Assert.NotEqual(callingThread, reader.ThreadId);
        }
        finally
        {
            releaseCaller.Set();
            Assert.True(caller.Join(TimeSpan.FromSeconds(5)));
        }
    }

    private sealed class ThreadRecordingEvidenceReader : IFileEvidenceReader
    {
        public int ThreadId { get; private set; }

        public ValueTask<FileReviewMetadata> ReadMetadataAsync(
            string path,
            CancellationToken cancellationToken)
        {
            ThreadId = Environment.CurrentManagedThreadId;
            return ValueTask.FromResult(new FileReviewMetadata(
                path,
                Path.GetFileName(path),
                16,
                DateTimeOffset.UnixEpoch,
                FileAttributes.Normal));
        }

        public ValueTask<ReadOnlyMemory<byte>> ReadHeaderAsync(
            string path,
            int maximumBytes,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty);

        public ValueTask<MarkOfTheWeb?> ReadMarkOfTheWebAsync(
            string path,
            CancellationToken cancellationToken) => ValueTask.FromResult<MarkOfTheWeb?>(null);

        public ValueTask<double?> ReadEntropyAsync(
            string path,
            int sampleSize,
            int maximumSamples,
            CancellationToken cancellationToken) => ValueTask.FromResult<double?>(null);

        public ValueTask<ShortcutMetadata?> ReadShortcutAsync(
            string path,
            CancellationToken cancellationToken) => ValueTask.FromResult<ShortcutMetadata?>(null);
    }

    private sealed class StubAuthenticodeVerifier : IAuthenticodeVerifier
    {
        public ValueTask<AuthenticodeResult> VerifyAsync(
            string path,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AuthenticodeResult(AuthenticodeState.Valid, 0));
    }
}
