using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Qsearch.App.Tests;

public sealed partial class LocalizationContractTests
{
    [Fact]
    public void LocaleDictionaries_HaveExactKeyAndPlaceholderParity()
    {
        Dictionary<string, string> english = Load("Strings.en.xaml");
        Dictionary<string, string> persian = Load("Strings.fa.xaml");

        Assert.Equal(english.Keys.Order(), persian.Keys.Order());
        Assert.All(
            english.Keys,
            key => Assert.Equal(Placeholders(english[key]), Placeholders(persian[key])));
        Assert.Contains("About.Privacy", english.Keys);
        Assert.Contains("About.Website", persian.Keys);
    }

    private static Dictionary<string, string> Load(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Resources", name);
        XDocument document = XDocument.Load(path);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Root!
            .Elements()
            .ToDictionary(
                element => element.Attribute(x + "Key")!.Value,
                element => element.Value,
                StringComparer.Ordinal);
    }

    private static string[] Placeholders(string value)
    {
        return PlaceholderPattern().Matches(value).Select(match => match.Value).Order().ToArray();
    }

    [GeneratedRegex("\\{[0-9]+\\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
