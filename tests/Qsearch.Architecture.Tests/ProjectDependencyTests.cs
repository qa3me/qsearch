using System.Xml.Linq;

namespace Qsearch.Architecture.Tests;

public sealed class ProjectDependencyTests
{
    [Fact]
    public void CoreIsIndependentAndSafetyDependsOnlyOnCore()
    {
        string root = RepositoryRoot.Find();

        Assert.Empty(ProjectReferences(root, "src/Qsearch.Core/Qsearch.Core.csproj"));
        Assert.Equal(
            ["..\\Qsearch.Core\\Qsearch.Core.csproj"],
            ProjectReferences(root, "src/Qsearch.Safety/Qsearch.Safety.csproj"));
    }

    private static string[] ProjectReferences(string root, string relativePath)
    {
        XDocument project = XDocument.Load(Path.Combine(root, relativePath));
        return project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")!.Value)
            .ToArray();
    }
}
