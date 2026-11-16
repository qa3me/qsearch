using Qsearch.Core.Text;

namespace Qsearch.Core.Tests.Text;

public sealed class SearchTextNormalizerTests
{
    [Theory]
    [InlineData("كیف", "کیف")]
    [InlineData("يادداشت", "یادداشت")]
    [InlineData("سِلامـ", "سلام")]
    public void Normalize_PersianVariants_AreSearchEquivalent(string input, string expected)
    {
        Assert.Equal(expected, SearchTextNormalizer.Normalize(input));
    }
}
