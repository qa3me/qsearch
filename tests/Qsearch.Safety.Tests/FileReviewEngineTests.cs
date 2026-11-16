using Qsearch.Safety.Models;

namespace Qsearch.Safety.Tests;

public sealed class FileReviewEngineTests
{
    [Fact]
    public async Task UnsignedExecutable_IsInformationalOnly()
    {
        FileReviewResult result = await TestReview.For("tool.exe").Unsigned().RunAsync();

        Assert.Equal(0, result.Score);
        Assert.Equal(ReviewState.NoNotableSignals, result.State);
        Assert.Contains(result.Evidence, evidence => evidence.RuleId == "signature.unsigned" && evidence.Score == 0);
    }

    [Fact]
    public async Task BidiAndDoubleExtension_ProducesHighCautionWithReasons()
    {
        FileReviewResult result = await TestReview
            .For("invoice\u202Efdp.exe")
            .Header(FileKind.PortableExecutable)
            .RunAsync();

        Assert.Equal(ReviewState.HighCaution, result.State);
        Assert.Contains(result.Evidence, evidence => evidence.RuleId == "name.bidi-control");
        Assert.Contains(result.Evidence, evidence => evidence.RuleId == "name.double-extension");
    }

    [Fact]
    public async Task ChangedDuringReview_IsIncompleteAndNotRiskSearchable()
    {
        FileReviewResult result = await TestReview
            .For("changed.exe")
            .ChangeAfterHeaderRead()
            .RunAsync();

        Assert.Equal(ReviewState.Incomplete, result.State);
        Assert.False(result.IsRiskSearchable);
    }
}
