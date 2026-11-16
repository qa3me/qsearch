using System.Reflection;
using Qsearch.Core.Search;
using Qsearch.Index.Sqlite;
using Qsearch.Safety;
using Qsearch.Windows.Indexing;

namespace Qsearch.Architecture.Tests;

public sealed class OfflineBoundaryTests
{
    [Fact]
    public void RuntimeAssemblies_ReferenceNoNetworkClientsEndpointsOrTelemetry()
    {
        string repositoryRoot = RepositoryRoot.Find();
        Assembly[] assemblies =
        [
            typeof(SearchQueryParser).Assembly,
            typeof(SqliteIndexStore).Assembly,
            typeof(FileReviewEngine).Assembly,
            typeof(IndexCoordinator).Assembly,
            typeof(Qsearch.App.App).Assembly,
        ];

        OfflineBoundaryResult audit = OfflineBoundaryAudit.Scan(assemblies, repositoryRoot);

        Assert.Empty(audit.NetworkAssemblyReferences);
        Assert.Empty(audit.RemoteEndpoints);
        Assert.Empty(audit.TelemetryPackages);
    }
}
