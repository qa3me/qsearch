using System.Data.Common;
using System.IO;
using Qsearch.Core.Indexing;

namespace Qsearch.App.Services;

public enum IndexInitializationResult
{
    Ready,
    Unavailable,
}

public static class BackgroundIndexInitializer
{
    public static async Task<IndexInitializationResult> InitializeAsync(
        IIndexStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            await Task.Run(
                () => store.InitializeAsync(cancellationToken).AsTask(),
                cancellationToken).ConfigureAwait(false);
            return IndexInitializationResult.Ready;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is DbException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return IndexInitializationResult.Unavailable;
        }
    }
}
