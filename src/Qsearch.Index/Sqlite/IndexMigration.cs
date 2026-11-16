using System.Globalization;
using Microsoft.Data.Sqlite;
using Qsearch.Core.Text;

namespace Qsearch.Index.Sqlite;

internal static class IndexMigration
{
    public static async ValueTask ApplyAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = IndexSchema.CreateVersionOne;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = IndexSchema.CreateVersionTwo;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT value FROM metadata WHERE key = 'normalization_version';";
        object? value = await versionCommand.ExecuteScalarAsync(cancellationToken);
        if (value is null ||
            !int.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int version) ||
            version != SearchTextNormalizer.Version)
        {
            throw new IndexRebuildRequiredException(
                "The search normalization version changed and the local index must be rebuilt.");
        }
    }
}
