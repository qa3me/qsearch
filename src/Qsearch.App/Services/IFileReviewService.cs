using Qsearch.Safety;
using Qsearch.Safety.Models;

namespace Qsearch.App.Services;

public interface IFileReviewService
{
    ValueTask<FileReviewResult> ReviewAsync(string path, CancellationToken cancellationToken);
}

public sealed class LocalFileReviewService : IFileReviewService
{
    private readonly FileReviewEngine _engine;

    public LocalFileReviewService(FileReviewEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
    }

    public async ValueTask<FileReviewResult> ReviewAsync(
        string path,
        CancellationToken cancellationToken)
    {
        return await Task.Run(
            async () => await _engine.ReviewAsync(
                new FileReviewRequest(path),
                cancellationToken).ConfigureAwait(false),
            CancellationToken.None).ConfigureAwait(false);
    }
}
