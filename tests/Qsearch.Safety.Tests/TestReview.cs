using Qsearch.Safety.Evidence;
using Qsearch.Safety.Models;
using Qsearch.Safety.Policy;

namespace Qsearch.Safety.Tests;

internal sealed class TestReview
{
    private readonly StubEvidenceReader _evidence;
    private AuthenticodeResult _signature = new(AuthenticodeState.NoEmbeddedSignature, unchecked((int)0x800B0100));

    private TestReview(string name)
    {
        string path = Path.Combine(@"C:\Users\QA3\Desktop", name);
        _evidence = new StubEvidenceReader(
            new FileReviewMetadata(
                path,
                name,
                256 * 1024,
                new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero),
                FileAttributes.Normal));
    }

    public static TestReview For(string name) => new(name);

    public TestReview Unsigned()
    {
        _signature = new AuthenticodeResult(AuthenticodeState.NoEmbeddedSignature, unchecked((int)0x800B0100));
        return this;
    }

    public TestReview Header(FileKind kind)
    {
        _evidence.Header = kind switch
        {
            FileKind.PortableExecutable => [0x4D, 0x5A, 0x90, 0x00],
            FileKind.PdfDocument => "%PDF"u8.ToArray(),
            _ => [],
        };
        return this;
    }

    public TestReview ChangeAfterHeaderRead()
    {
        _evidence.ChangeAfterHeader = true;
        return this;
    }

    public async ValueTask<FileReviewResult> RunAsync()
    {
        FileReviewEngine engine = new(
            _evidence,
            new StubAuthenticodeVerifier(_signature),
            ReviewPolicy.VersionOne,
            TimeProvider.System);
        return await engine.ReviewAsync(
            new FileReviewRequest(_evidence.Metadata.Path),
            TestContext.Current.CancellationToken);
    }

    private sealed class StubAuthenticodeVerifier(AuthenticodeResult result) : IAuthenticodeVerifier
    {
        public ValueTask<AuthenticodeResult> VerifyAsync(
            string path,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StubEvidenceReader(FileReviewMetadata metadata) : IFileEvidenceReader
    {
        private int _metadataReads;

        public FileReviewMetadata Metadata { get; } = metadata;

        public byte[] Header { get; set; } = [0x4D, 0x5A, 0x90, 0x00];

        public bool ChangeAfterHeader { get; set; }

        public ValueTask<FileReviewMetadata> ReadMetadataAsync(
            string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _metadataReads++;
            return ValueTask.FromResult(
                ChangeAfterHeader && _metadataReads > 1
                    ? Metadata with { Size = Metadata.Size + 1 }
                    : Metadata);
        }

        public ValueTask<ReadOnlyMemory<byte>> ReadHeaderAsync(
            string path,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(Header.AsMemory(0, Math.Min(Header.Length, maximumBytes)));
        }

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
}
