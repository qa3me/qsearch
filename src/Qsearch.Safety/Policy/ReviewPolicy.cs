using Qsearch.Safety.Models;

namespace Qsearch.Safety.Policy;

public sealed record ReviewRule(
    string Id,
    int Score,
    string Explanation,
    string Guidance);

public sealed class ReviewPolicy
{
    private readonly Dictionary<string, ReviewRule> _rulesById;

    private ReviewPolicy(
        int version,
        int reviewSuggestedThreshold,
        int highCautionThreshold,
        IReadOnlyList<ReviewRule> rules)
    {
        Version = version;
        ReviewSuggestedThreshold = reviewSuggestedThreshold;
        HighCautionThreshold = highCautionThreshold;
        Rules = rules;
        _rulesById = rules.ToDictionary(rule => rule.Id, StringComparer.Ordinal);
    }

    public static ReviewPolicy VersionOne { get; } = new(
        1,
        20,
        50,
        [
            Rule("name.bidi-control", 40, "The displayed name contains a bidirectional control character.", "Compare the displayed name with its exact stored characters."),
            Rule("name.double-extension", 25, "The name combines a familiar document extension with executable or script context.", "Review the final extension and recognized content type."),
            Rule("content.header-conflict", 30, "The recognized file header differs from the type suggested by the extension.", "Use the recognized header and extension as separate context."),
            Rule("signature.integrity-failure", 45, "Local Authenticode validation returned an integrity or trust failure.", "Inspect the exact Windows status and publisher details."),
            Rule("signature.unsigned", 0, "No embedded Authenticode signature was found.", "Treat signature presence as publisher context, not a verdict."),
            Rule("origin.mark-of-web", 5, "Windows origin metadata indicates an internet or restricted zone.", "Use origin information as context for further review."),
            Rule("location.startup", 35, "Executable or script content is located in a Startup folder.", "Confirm whether automatic launch from this location is expected."),
            Rule("location.temporary", 15, "Executable or script content is located in a temporary folder.", "Confirm why executable content is stored in this location."),
            Rule("location.downloads", 5, "Executable or script content is located in Downloads.", "Use the download location as origin context."),
            Rule("attributes.hidden-system", 10, "Executable or script content has a hidden or system attribute.", "Review the attributes together with the file's purpose."),
            Rule("document.macro-with-origin", 15, "A macro-capable document also carries internet origin metadata.", "Review the document source before enabling active content."),
            Rule("shortcut.script-host", 25, "Shortcut metadata points to a script host or shell command.", "Inspect the displayed target and arguments without launching them."),
            Rule("name.unusual-presentation", 20, "The stored name uses a trailing dot, space, or reserved-name presentation.", "Compare the exact stored name with its Explorer presentation."),
            Rule("content.high-entropy", 10, "Bounded samples have high entropy, which is weak evidence and may reflect compression.", "Treat entropy only as supporting context."),
            Rule("category.executable-context", 0, "The file is categorized as executable, script, shortcut, or installer content.", "Use the category to interpret other evidence."),
        ]);

    public int Version { get; }

    public IReadOnlyList<ReviewRule> Rules { get; }

    public int ReviewSuggestedThreshold { get; }

    public int HighCautionThreshold { get; }

    public ReviewRule GetRule(string id)
    {
        return _rulesById[id];
    }

    public ReviewState MapState(int score)
    {
        return score switch
        {
            _ when score < ReviewSuggestedThreshold => ReviewState.NoNotableSignals,
            _ when score < HighCautionThreshold => ReviewState.ReviewSuggested,
            _ => ReviewState.HighCaution,
        };
    }

    private static ReviewRule Rule(string id, int score, string explanation, string guidance)
    {
        return new ReviewRule(id, score, explanation, guidance);
    }
}
