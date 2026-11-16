using Qsearch.Safety.Models;
using Qsearch.Safety.Policy;

namespace Qsearch.Safety.Tests;

public sealed class ReviewPolicyTests
{
    [Fact]
    public void VersionOne_ContainsEveryPinnedRuleWeight()
    {
        Dictionary<string, int> expected = new(StringComparer.Ordinal)
        {
            ["name.bidi-control"] = 40,
            ["name.double-extension"] = 25,
            ["content.header-conflict"] = 30,
            ["signature.integrity-failure"] = 45,
            ["signature.unsigned"] = 0,
            ["origin.mark-of-web"] = 5,
            ["location.startup"] = 35,
            ["location.temporary"] = 15,
            ["location.downloads"] = 5,
            ["attributes.hidden-system"] = 10,
            ["document.macro-with-origin"] = 15,
            ["shortcut.script-host"] = 25,
            ["name.unusual-presentation"] = 20,
            ["content.high-entropy"] = 10,
            ["category.executable-context"] = 0,
        };

        Assert.Equal(expected, ReviewPolicy.VersionOne.Rules.ToDictionary(rule => rule.Id, rule => rule.Score));
    }

    [Theory]
    [InlineData(0, ReviewState.NoNotableSignals)]
    [InlineData(19, ReviewState.NoNotableSignals)]
    [InlineData(20, ReviewState.ReviewSuggested)]
    [InlineData(49, ReviewState.ReviewSuggested)]
    [InlineData(50, ReviewState.HighCaution)]
    public void ScoreBoundaries_AreExact(int score, ReviewState expected)
    {
        Assert.Equal(expected, ReviewPolicy.VersionOne.MapState(score));
    }

    [Fact]
    public void UserFacingPolicyText_AvoidsVerdictLanguage()
    {
        string[] prohibited = ["malware", "infected", "clean", "safe", "بدافزار", "آلوده", "سالم", "امن"];
        IEnumerable<string> text = ReviewPolicy.VersionOne.Rules
            .SelectMany(rule => new[] { rule.Explanation, rule.Guidance })
            .Concat(Enum.GetNames<ReviewState>())
            .Concat(typeof(FileReviewResult).GetProperties().Select(property => property.Name));

        foreach (string value in text)
        {
            Assert.DoesNotContain(
                prohibited,
                term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
    }
}
