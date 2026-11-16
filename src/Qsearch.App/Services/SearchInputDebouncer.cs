namespace Qsearch.App.Services;

public sealed class SearchInputDebouncer : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _pending;
    private bool _disposed;

    public SearchInputDebouncer(TimeSpan delay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        _delay = delay;
    }

    public async ValueTask SubmitAsync(
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        CancellationTokenSource next;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            next = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationTokenSource? previous = _pending;
            _pending = next;
            previous?.Cancel();
        }

        try
        {
            await Task.Delay(_delay, next.Token);
            await action(next.Token);
        }
        catch (OperationCanceledException) when (next.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pending, next))
                {
                    _pending = null;
                }
            }

            next.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending?.Cancel();
            _pending = null;
        }
    }
}
