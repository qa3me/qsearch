using Qsearch.App.ViewModels;
using Qsearch.Core.Indexing;

namespace Qsearch.App.Tests;

public sealed class ResultVisualClassifierTests
{
    [Theory]
    [InlineData("folder.with.dots", IndexItemType.Folder, ResultVisualKind.Folder)]
    [InlineData("TOOL.EXE", IndexItemType.File, ResultVisualKind.Application)]
    [InlineData("setup.MSI", IndexItemType.File, ResultVisualKind.Application)]
    [InlineData("shortcut.LNK", IndexItemType.File, ResultVisualKind.Application)]
    [InlineData("LICENSE", IndexItemType.File, ResultVisualKind.File)]
    [InlineData("report.pdf", IndexItemType.File, ResultVisualKind.File)]
    public void Classify_UsesTypeBeforeExtensionAndRecognizesLaunchableFiles(
        string name,
        IndexItemType type,
        ResultVisualKind expected)
    {
        Assert.Equal(expected, ResultVisualClassifier.Classify(name, type));
    }
}
