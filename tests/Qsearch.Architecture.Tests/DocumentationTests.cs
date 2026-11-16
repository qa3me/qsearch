namespace Qsearch.Architecture.Tests;

public sealed class DocumentationTests
{
    [Fact]
    public void RepositoryLanding_ReferencesNonEmptyEnglishAndPersianProductRenders()
    {
        string root = RepositoryRoot.Find();
        string englishReadme = File.ReadAllText(Path.Combine(root, "README.md"));
        string persianReadme = File.ReadAllText(Path.Combine(root, "README.fa.md"));
        string[] relativePaths =
        [
            "docs/images/qsearch-search-en.png",
            "docs/images/qsearch-search-fa.png",
        ];

        Assert.All(relativePaths, relativePath =>
        {
            Assert.Contains(relativePath, englishReadme, StringComparison.Ordinal);
            Assert.Contains(relativePath, persianReadme, StringComparison.Ordinal);
            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), relativePath);
            Assert.True(new FileInfo(path).Length > 10_000, relativePath);
            Assert.Equal(
                [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A],
                File.ReadAllBytes(path).Take(8).ToArray());
        });
    }

    [Fact]
    public void Documentation_StatesOfflineBoundaryAndNonVerdictLimitsInBothLanguages()
    {
        string root = RepositoryRoot.Find();
        string english = File.ReadAllText(Path.Combine(root, "README.md"));
        string persian = File.ReadAllText(Path.Combine(root, "README.fa.md"));

        Assert.Contains("fully offline", english, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "does not determine whether a file is safe or malicious",
            english,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("کاملاً آفلاین", persian, StringComparison.Ordinal);
        Assert.Contains("حکم قطعی", persian, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_HasRequiredSecurityPrivacyAndReleaseGuides()
    {
        string root = RepositoryRoot.Find();
        string[] required =
        [
            "PRIVACY.md",
            "SECURITY.md",
            "THREAT-MODEL.md",
            "ASSETS-NOTICE.md",
            "docs/architecture.md",
            "docs/development.md",
            "docs/packaging-and-signing.md",
            "docs/release.md",
        ];

        Assert.All(required, path => Assert.True(File.Exists(Path.Combine(root, path)), path));
    }

    [Fact]
    public void Documentation_ExplainsMachineWideLocalIndexAndBothPackageChoices()
    {
        string root = RepositoryRoot.Find();
        string english = File.ReadAllText(Path.Combine(root, "README.md"));
        string persian = File.ReadAllText(Path.Combine(root, "README.fa.md"));
        string privacy = File.ReadAllText(Path.Combine(root, "PRIVACY.md"));
        string packaging = File.ReadAllText(Path.Combine(root, "docs", "packaging-and-signing.md"));

        Assert.Contains("fixed NTFS volumes", english, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("دیسک‌های ثابت NTFS", persian, StringComparison.Ordinal);
        Assert.Contains("file names", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never transmitted", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("slim", packaging, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Microsoft.WindowsDesktop.App", packaging, StringComparison.Ordinal);
        Assert.Contains("self-contained", packaging, StringComparison.OrdinalIgnoreCase);
    }
}
