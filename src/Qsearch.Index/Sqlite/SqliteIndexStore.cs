using System.Globalization;
using Microsoft.Data.Sqlite;
using Qsearch.Core.Indexing;
using Qsearch.Core.Text;
using Qsearch.Index.Search;

namespace Qsearch.Index.Sqlite;

public sealed class SqliteIndexStore : IIndexStore, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly string _databasePath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public SqliteIndexStore(string databasePath, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _databasePath = Path.GetFullPath(databasePath);
        _timeProvider = timeProvider;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ConnectionString;
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _writer.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            string? directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await IndexMigration.ApplyAsync(connection, cancellationToken);
            _initialized = true;
        }
        finally
        {
            _writer.Release();
        }
    }

    public async ValueTask ApplyBatchAsync(
        IndexBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        EnsureInitialized();
        await _writer.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await EnsureScopeAsync(connection, transaction, batch.ScopeId, cancellationToken);
                await using SqliteCommand upsertCommand = CreateUpsertCommand(connection, transaction);
                await using SqliteCommand deleteCommand = CreateDeleteCommand(connection, transaction);
                foreach (IndexChange change in batch.Changes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!string.Equals(change.ScopeId, batch.ScopeId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Every change must belong to the batch scope.");
                    }

                    switch (change.Kind)
                    {
                        case IndexChangeKind.Upsert:
                            await UpsertAsync(upsertCommand, change, cancellationToken);
                            break;
                        case IndexChangeKind.Delete:
                            await DeleteAsync(deleteCommand, change, cancellationToken);
                            break;
                        case IndexChangeKind.Diagnostic:
                        case IndexChangeKind.Checkpoint:
                            break;
                        default:
                            throw new InvalidOperationException("Unsupported index change kind.");
                    }
                }

                if (batch.Checkpoint is not null)
                {
                    await SaveCheckpointCoreAsync(
                        connection,
                        transaction,
                        batch.Checkpoint,
                        cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            _writer.Release();
        }
    }

    public async ValueTask<SearchPage> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();

        SearchSql sql = SearchSqlBuilder.Build(request);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql.CommandText;
        foreach ((string name, object value) in sql.Parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        List<SearchResult> results = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SearchResult(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                (IndexItemType)reader.GetInt32(4),
                reader.GetInt64(5),
                DateTimeOffset.Parse(
                    reader.GetString(6),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)));
        }

        int requestedLimit = Math.Clamp(request.Limit, 1, 100);
        bool hasMore = results.Count > requestedLimit;
        if (hasMore)
        {
            results.RemoveRange(requestedLimit, results.Count - requestedLimit);
        }

        string? continuationToken = hasMore && results.Count > 0
            ? SearchSqlBuilder.CreateContinuationToken(request, results[^1])
            : null;
        return new SearchPage(results, IndexFreshness.Fresh, continuationToken);
    }

    public async ValueTask<IndexCheckpoint?> GetCheckpointAsync(
        string scopeId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        EnsureInitialized();
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT scope_id, journal_id, next_usn, recorded_at_utc
            FROM checkpoints
            WHERE scope_id = $scope_id;
            """;
        command.Parameters.AddWithValue("$scope_id", scopeId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new IndexCheckpoint(
            reader.GetString(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            DateTimeOffset.Parse(
                reader.GetString(3),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));
    }

    public async ValueTask SaveCheckpointAsync(
        IndexCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        EnsureInitialized();
        await _writer.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await EnsureScopeAsync(connection, transaction, checkpoint.ScopeId, cancellationToken);
            await SaveCheckpointCoreAsync(connection, transaction, checkpoint, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _writer.Release();
        }
    }

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        EnsureInitialized();
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM items;";
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public async ValueTask ResetScopeAsync(
        string scopeId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        EnsureInitialized();
        await _writer.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM checkpoints WHERE scope_id = $scope_id;
                DELETE FROM items WHERE scope_id = $scope_id;
                """;
            command.Parameters.AddWithValue("$scope_id", scopeId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _writer.Release();
        }
    }

    public async ValueTask<ReviewCacheRecord?> GetReviewAsync(
        string itemId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        EnsureInitialized();
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT item_id, state, is_complete, size, last_write_utc, policy_version
            FROM review_cache
            WHERE item_id = $item_id;
            """;
        command.Parameters.AddWithValue("$item_id", itemId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ReviewCacheRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetInt64(3),
            DateTimeOffset.Parse(
                reader.GetString(4),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            reader.GetInt32(5));
    }

    public async ValueTask SaveReviewAsync(
        ReviewCacheRecord review,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);
        EnsureInitialized();
        await _writer.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO review_cache(
                    item_id, state, is_complete, size, last_write_utc, policy_version)
                VALUES(
                    $item_id, $state, $is_complete, $size, $last_write_utc, $policy_version)
                ON CONFLICT(item_id) DO UPDATE SET
                    state = excluded.state,
                    is_complete = excluded.is_complete,
                    size = excluded.size,
                    last_write_utc = excluded.last_write_utc,
                    policy_version = excluded.policy_version;
                """;
            command.Parameters.AddWithValue("$item_id", review.ItemId);
            command.Parameters.AddWithValue("$state", review.State);
            command.Parameters.AddWithValue("$is_complete", review.IsComplete);
            command.Parameters.AddWithValue("$size", review.Size);
            command.Parameters.AddWithValue(
                "$last_write_utc",
                review.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$policy_version", review.PolicyVersion);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writer.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _writer.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async ValueTask EnsureScopeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string scopeId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO scopes(id, root_path, freshness)
            VALUES($id, '', 0)
            ON CONFLICT(id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", scopeId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteCommand CreateUpsertCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO items(
                id, scope_id, parent_id, name, normalized_name, full_path, extension,
                item_type, size, last_write_utc, attributes, path_depth)
            VALUES(
                $id, $scope_id, $parent_id, $name, $normalized_name, $full_path, $extension,
                $item_type, $size, $last_write_utc, $attributes, $path_depth)
            ON CONFLICT(id) DO UPDATE SET
                scope_id = excluded.scope_id,
                parent_id = excluded.parent_id,
                name = excluded.name,
                normalized_name = excluded.normalized_name,
                full_path = excluded.full_path,
                extension = excluded.extension,
                item_type = excluded.item_type,
                size = excluded.size,
                last_write_utc = excluded.last_write_utc,
                attributes = excluded.attributes,
                path_depth = excluded.path_depth
            WHERE items.scope_id IS NOT excluded.scope_id
                OR items.parent_id IS NOT excluded.parent_id
                OR items.name IS NOT excluded.name
                OR items.normalized_name IS NOT excluded.normalized_name
                OR items.full_path IS NOT excluded.full_path
                OR items.extension IS NOT excluded.extension
                OR items.item_type IS NOT excluded.item_type
                OR items.size IS NOT excluded.size
                OR items.last_write_utc IS NOT excluded.last_write_utc
                OR items.attributes IS NOT excluded.attributes
                OR items.path_depth IS NOT excluded.path_depth;
            """;
        command.Parameters.Add("$id", SqliteType.Text);
        command.Parameters.Add("$scope_id", SqliteType.Text);
        command.Parameters.Add("$parent_id", SqliteType.Text);
        command.Parameters.Add("$name", SqliteType.Text);
        command.Parameters.Add("$normalized_name", SqliteType.Text);
        command.Parameters.Add("$full_path", SqliteType.Text);
        command.Parameters.Add("$extension", SqliteType.Text);
        command.Parameters.Add("$item_type", SqliteType.Integer);
        command.Parameters.Add("$size", SqliteType.Integer);
        command.Parameters.Add("$last_write_utc", SqliteType.Text);
        command.Parameters.Add("$attributes", SqliteType.Integer);
        command.Parameters.Add("$path_depth", SqliteType.Integer);
        command.Prepare();
        return command;
    }

    private static SqliteCommand CreateDeleteCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM items WHERE id = $id AND scope_id = $scope_id;";
        command.Parameters.Add("$id", SqliteType.Text);
        command.Parameters.Add("$scope_id", SqliteType.Text);
        command.Prepare();
        return command;
    }

    private static async ValueTask UpsertAsync(
        SqliteCommand command,
        IndexChange change,
        CancellationToken cancellationToken)
    {
        IndexItem item = change.Item ??
            throw new InvalidOperationException("An upsert change requires an item.");
        if (!string.Equals(item.ScopeId, change.ScopeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The item and change scope must match.");
        }

        command.Parameters["$id"].Value = item.Id;
        command.Parameters["$scope_id"].Value = item.ScopeId;
        command.Parameters["$parent_id"].Value = (object?)item.ParentId ?? DBNull.Value;
        command.Parameters["$name"].Value = item.Name;
        command.Parameters["$normalized_name"].Value = item.NormalizedName;
        command.Parameters["$full_path"].Value = item.FullPath;
        command.Parameters["$extension"].Value = item.Type == IndexItemType.File
            ? SearchTextNormalizer.Normalize(Path.GetExtension(item.Name).TrimStart('.'))
            : string.Empty;
        command.Parameters["$item_type"].Value = (int)item.Type;
        command.Parameters["$size"].Value = item.Size;
        command.Parameters["$last_write_utc"].Value =
            item.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters["$attributes"].Value = (long)item.Attributes;
        command.Parameters["$path_depth"].Value =
            item.FullPath.Count(character => character is '\\' or '/');
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async ValueTask DeleteAsync(
        SqliteCommand command,
        IndexChange change,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(change.ItemId))
        {
            throw new InvalidOperationException("A delete change requires an item ID.");
        }

        command.Parameters["$id"].Value = change.ItemId;
        command.Parameters["$scope_id"].Value = change.ScopeId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask SaveCheckpointCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IndexCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO checkpoints(scope_id, journal_id, next_usn, recorded_at_utc)
            VALUES($scope_id, $journal_id, $next_usn, $recorded_at_utc)
            ON CONFLICT(scope_id) DO UPDATE SET
                journal_id = excluded.journal_id,
                next_usn = excluded.next_usn,
                recorded_at_utc = excluded.recorded_at_utc;
            """;
        command.Parameters.AddWithValue("$scope_id", checkpoint.ScopeId);
        command.Parameters.AddWithValue("$journal_id", checkpoint.JournalId);
        command.Parameters.AddWithValue("$next_usn", checkpoint.NextUsn);
        command.Parameters.AddWithValue(
            "$recorded_at_utc",
            (checkpoint.RecordedAtUtc ?? _timeProvider.GetUtcNow())
                .ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsureInitialized()
    {
        ThrowIfDisposed();
        if (!_initialized)
        {
            throw new InvalidOperationException("Initialize the index store before using it.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
