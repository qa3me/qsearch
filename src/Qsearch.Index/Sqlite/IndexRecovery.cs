using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Qsearch.Index.Sqlite;

public enum IndexOpenState
{
    Ready,
    RebuildRequired,
}

public sealed record IndexOpenResult(IndexOpenState State, string? PreservedPath = null);

public sealed class IndexRebuildRequiredException : Exception
{
    public IndexRebuildRequiredException()
    {
    }

    public IndexRebuildRequiredException(string message)
        : base(message)
    {
    }

    public IndexRebuildRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class IndexRecovery
{
    public static async ValueTask<IndexOpenResult> OpenOrPreserveAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(databasePath))
        {
            return new IndexOpenResult(IndexOpenState.Ready);
        }

        try
        {
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            };
            await using SqliteConnection connection = new(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            if (string.Equals(
                    Convert.ToString(result, CultureInfo.InvariantCulture),
                    "ok",
                    StringComparison.Ordinal))
            {
                return new IndexOpenResult(IndexOpenState.Ready);
            }
        }
        catch (SqliteException)
        {
        }

        string preservedPath = FindPreservedPath(databasePath);
        File.Move(databasePath, preservedPath);
        MoveSidecar(databasePath + "-wal", preservedPath + "-wal");
        MoveSidecar(databasePath + "-shm", preservedPath + "-shm");
        return new IndexOpenResult(IndexOpenState.RebuildRequired, preservedPath);
    }

    private static string FindPreservedPath(string databasePath)
    {
        string candidate = databasePath + ".corrupt";
        for (int suffix = 1; File.Exists(candidate); suffix++)
        {
            candidate = $"{databasePath}.corrupt-{suffix}";
        }

        return candidate;
    }

    private static void MoveSidecar(string source, string destination)
    {
        if (File.Exists(source))
        {
            File.Move(source, destination);
        }
    }
}
