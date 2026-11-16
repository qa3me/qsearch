using Qsearch.Core.Indexing;

namespace Qsearch.Windows.Indexing;

public sealed record IndexCoordinatorState(IndexFreshness Freshness);

public sealed class IndexCoordinator
{
    private const int MaximumBatchSize = 2000;
    private static readonly TimeSpan MaximumBatchAge = TimeSpan.FromMilliseconds(100);
    private readonly IIndexStore _store;
    private readonly TimeProvider _timeProvider;

    public IndexCoordinator(IIndexStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _timeProvider = timeProvider;
    }

    public IndexCoordinatorState State { get; private set; } = new(IndexFreshness.Unavailable);

    public event Action<IndexCoordinatorState>? StateChanged;

    public async ValueTask RunAsync(
        IndexScope scope,
        IIndexSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(source);

        while (true)
        {
            SetState(IndexFreshness.CatchingUp);
            await ConsumeAsync(
                scope.Id,
                source.ScanAsync(scope, cancellationToken),
                cancellationToken);
            SetState(IndexFreshness.Fresh);

            IndexCheckpoint? checkpoint = await _store.GetCheckpointAsync(
                scope.Id,
                cancellationToken);
            bool rebuildRequired = await ConsumeAsync(
                scope.Id,
                source.WatchAsync(scope, checkpoint, cancellationToken),
                cancellationToken);
            if (!rebuildRequired)
            {
                break;
            }

            SetState(IndexFreshness.Stale);
            await _store.ResetScopeAsync(scope.Id, cancellationToken);
        }
    }

    private async ValueTask<bool> ConsumeAsync(
        string scopeId,
        IAsyncEnumerable<IndexChange> source,
        CancellationToken cancellationToken)
    {
        List<IndexChange> batch = new(MaximumBatchSize);
        bool rebuildRequired = false;
        await using IAsyncEnumerator<IndexChange> enumerator =
            source.GetAsyncEnumerator(cancellationToken);
        Task<bool>? pendingMove = null;

        while (true)
        {
            pendingMove ??= enumerator.MoveNextAsync().AsTask();
            if (!await pendingMove)
            {
                return rebuildRequired;
            }

            using CancellationTokenSource deadlineCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task deadline = Task.Delay(
                MaximumBatchAge,
                _timeProvider,
                deadlineCancellation.Token);
            try
            {
                do
                {
                    pendingMove = null;
                    IndexChange change = enumerator.Current;
                    AddChange(batch, change);
                    rebuildRequired |= IsRebuildRequired(change);
                    if (batch.Count >= MaximumBatchSize || deadline.IsCompleted)
                    {
                        break;
                    }

                    pendingMove = enumerator.MoveNextAsync().AsTask();
                    if (!pendingMove.IsCompleted)
                    {
                        await Task.WhenAny(pendingMove, deadline);
                    }
                }
                while (!deadline.IsCompleted && await pendingMove);
            }
            finally
            {
                await deadlineCancellation.CancelAsync();
            }

            await FlushAsync(scopeId, batch, cancellationToken);
        }
    }

    private static bool IsRebuildRequired(IndexChange change) =>
        change.Diagnostic?.Code == "journal-rebuild-required";

    private void AddChange(List<IndexChange> batch, IndexChange change)
    {
        batch.Add(change);
        if (change.Diagnostic?.Code is "watcher-overflow" or "watcher-error")
        {
            SetState(IndexFreshness.Stale);
        }
    }

    private async ValueTask FlushAsync(
        string scopeId,
        List<IndexChange> batch,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        IndexChange[] changes = batch.ToArray();
        IndexCheckpoint? checkpoint = changes
            .LastOrDefault(change => change.Checkpoint is not null)
            ?.Checkpoint;
        batch.Clear();
        await _store.ApplyBatchAsync(
            new IndexBatch(scopeId, changes, checkpoint),
            cancellationToken);
    }

    private void SetState(IndexFreshness freshness)
    {
        State = new IndexCoordinatorState(freshness);
        StateChanged?.Invoke(State);
    }
}
