using Qsearch.Core.Indexing;

namespace Qsearch.App.Services;

public enum MachineIndexStartResult
{
    Started,
    ConsentRequired,
    NoVolumes,
    ElevationDeclined,
    Unavailable,
}

public sealed record IndexingSnapshot(
    long IndexedItems,
    int TotalVolumes,
    int FreshVolumes,
    IndexFreshness Freshness,
    double? Progress,
    IReadOnlyList<double> RecentRates);

public interface IMachineIndexingService : IAsyncDisposable
{
    event Action<IndexingSnapshot>? SnapshotChanged;

    IndexingSnapshot Snapshot { get; }

    Task Completion { get; }

    ValueTask<MachineIndexStartResult> StartAsync(CancellationToken cancellationToken);

    ValueTask<MachineIndexStartResult> RetryElevationAsync(CancellationToken cancellationToken);

    ValueTask RebuildAsync(CancellationToken cancellationToken);
}
