using Qsearch.Core.Indexing;

namespace Qsearch.Windows.Indexing;

public sealed record ReconciliationState(IndexFreshness Freshness);

public sealed class ScopeReconciler
{
    private readonly IChangeNotificationSource _source;
    private readonly List<string> _scheduledScopes = [];

    public ScopeReconciler(IChangeNotificationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public ReconciliationState State { get; private set; } = new(IndexFreshness.Fresh);

    public IReadOnlyList<string> ScheduledScopes => _scheduledScopes;

    public async ValueTask RunOnceAsync(CancellationToken cancellationToken)
    {
        await foreach (ChangeFeedEvent change in _source.WatchAsync(cancellationToken))
        {
            if (change.Kind is ChangeFeedEventKind.Overflow or ChangeFeedEventKind.Error)
            {
                State = new ReconciliationState(IndexFreshness.Stale);
                if (!_scheduledScopes.Contains(change.ScopeId, StringComparer.Ordinal))
                {
                    _scheduledScopes.Add(change.ScopeId);
                }
            }

            return;
        }
    }
}
