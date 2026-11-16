using Microsoft.Data.Sqlite;
using Qsearch.Index.Sqlite;

namespace Qsearch.Index.Tests;

public sealed class IndexRecoveryTests
{
    [Fact]
    public async Task CorruptDatabase_IsPreservedAndRequestsRebuild()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Qsearch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "index.db");
        await File.WriteAllBytesAsync(path, [0x13, 0x37], TestContext.Current.CancellationToken);

        try
        {
            IndexOpenResult result = await IndexRecovery.OpenOrPreserveAsync(
                path,
                TestContext.Current.CancellationToken);

            Assert.Equal(IndexOpenState.RebuildRequired, result.State);
            Assert.NotNull(result.PreservedPath);
            Assert.True(File.Exists(result.PreservedPath));
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NormalizationVersionMismatch_RequiresRebuild()
    {
        await using TestIndex index = await TestIndex.CreateAsync();
        await using (SqliteConnection connection = new($"Data Source={index.DatabasePath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE metadata SET value = '0' WHERE key = 'normalization_version';";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using SqliteIndexStore reopened = new(index.DatabasePath, TimeProvider.System);
        await Assert.ThrowsAsync<IndexRebuildRequiredException>(
            () => reopened.InitializeAsync(TestContext.Current.CancellationToken).AsTask());
    }
}
