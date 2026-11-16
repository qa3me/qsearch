using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Qsearch.Core.Indexing;

namespace Qsearch.Windows.Indexing;

public enum ChangeFeedEventKind
{
    Created,
    Changed,
    Deleted,
    Renamed,
    Overflow,
    Error,
}

public sealed record ChangeFeedEvent(
    string ScopeId,
    ChangeFeedEventKind Kind,
    string? FullPath = null,
    string? OldFullPath = null,
    IndexDiagnostic? Diagnostic = null)
{
    public static ChangeFeedEvent Overflow(string scopeId)
    {
        return new ChangeFeedEvent(
            scopeId,
            ChangeFeedEventKind.Overflow,
            Diagnostic: new IndexDiagnostic(
                "watcher-overflow",
                "File change notifications overflowed; this scope needs reconciliation."));
    }
}

public interface IChangeNotificationSource
{
    IAsyncEnumerable<ChangeFeedEvent> WatchAsync(CancellationToken cancellationToken);
}

public sealed class FileSystemChangeFeed : IChangeNotificationSource
{
    private readonly IndexScope _scope;

    public FileSystemChangeFeed(IndexScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    public async IAsyncEnumerable<ChangeFeedEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<ChangeFeedEvent> channel = Channel.CreateBounded<ChangeFeedEvent>(
            new BoundedChannelOptions(4096)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            });
        using FileSystemWatcher watcher = new(_scope.RootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName |
                NotifyFilters.DirectoryName |
                NotifyFilters.LastWrite |
                NotifyFilters.Size,
        };

        watcher.Created += (_, eventArgs) =>
            TryWrite(channel, new ChangeFeedEvent(_scope.Id, ChangeFeedEventKind.Created, eventArgs.FullPath));
        watcher.Changed += (_, eventArgs) =>
            TryWrite(channel, new ChangeFeedEvent(_scope.Id, ChangeFeedEventKind.Changed, eventArgs.FullPath));
        watcher.Deleted += (_, eventArgs) =>
            TryWrite(channel, new ChangeFeedEvent(_scope.Id, ChangeFeedEventKind.Deleted, eventArgs.FullPath));
        watcher.Renamed += (_, eventArgs) =>
            TryWrite(
                channel,
                new ChangeFeedEvent(
                    _scope.Id,
                    ChangeFeedEventKind.Renamed,
                    eventArgs.FullPath,
                    eventArgs.OldFullPath));
        watcher.Error += (_, eventArgs) =>
        {
            Exception exception = eventArgs.GetException();
            ChangeFeedEvent change = exception is InternalBufferOverflowException
                ? ChangeFeedEvent.Overflow(_scope.Id)
                : new ChangeFeedEvent(
                    _scope.Id,
                    ChangeFeedEventKind.Error,
                    Diagnostic: new IndexDiagnostic(
                        "watcher-error",
                        "File change notifications stopped and require reconciliation."));
            TryWrite(channel, change);
        };

        using CancellationTokenRegistration registration = cancellationToken.Register(
            () => channel.Writer.TryComplete());
        watcher.EnableRaisingEvents = true;
        await foreach (ChangeFeedEvent change in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return change;
        }
    }

    private static void TryWrite(Channel<ChangeFeedEvent> channel, ChangeFeedEvent change)
    {
        if (!channel.Writer.TryWrite(change))
        {
            channel.Writer.TryWrite(ChangeFeedEvent.Overflow(change.ScopeId));
        }
    }
}
