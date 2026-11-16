using System.IO.Compression;

namespace Qsearch.Architecture.Tests;

public sealed class PackagingTests
{
    [Fact]
    public void LegalFilesStager_CopiesCommonLicensesAndNotices()
    {
        string root = RepositoryRoot.Find();
        string script = Path.Combine(root, "packaging", "stage-legal-files.ps1");
        Assert.True(File.Exists(script), script);

        string destination = Path.Combine(Path.GetTempPath(), "Qsearch-legal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        try
        {
            using System.Diagnostics.Process process = new()
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("pwsh")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(script);
            process.StartInfo.ArgumentList.Add("-Destination");
            process.StartInfo.ArgumentList.Add(destination);

            Assert.True(process.Start());
            Assert.True(process.WaitForExit(15_000), "Legal-file staging timed out.");
            string standardError = process.StandardError.ReadToEnd();
            Assert.True(process.ExitCode == 0, standardError);

            string[] required =
            [
                "THIRD-PARTY-NOTICES.md",
                "licenses/Microsoft.Data.Sqlite/MIT.txt",
                "licenses/SQLitePCLRaw/Apache-2.0.txt",
                "licenses/FontAwesome-Free/LICENSE.txt",
                "licenses/FontAwesome-Free/ATTRIBUTION.md",
            ];
            Assert.All(required, relativePath =>
            {
                string path = Path.Combine(destination, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(path), relativePath);
                Assert.True(new FileInfo(path).Length > 0, relativePath);
            });

            string notices = File.ReadAllText(Path.Combine(destination, "THIRD-PARTY-NOTICES.md"));
            Assert.Contains("Microsoft.Data.Sqlite 10.0.15", notices, StringComparison.Ordinal);
            Assert.Contains("SQLitePCLRaw 2.1.13", notices, StringComparison.Ordinal);
            Assert.Contains("Font Awesome Free 7.3.1", notices, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(destination, recursive: true);
        }
    }

    [Fact]
    public void FontAwesomeAttribution_CoversEveryVendoredGeometry()
    {
        string root = RepositoryRoot.Find();
        string attribution = File.ReadAllText(
            Path.Combine(root, "assets", "fontawesome", "ATTRIBUTION.md"));

        string[] icons =
        [
            "folder",
            "file",
            "window-maximize",
            "shield-halved",
            "xmark",
            "arrow-up-right-from-square",
            "gear",
            "circle-info",
            "github",
        ];

        Assert.All(icons, icon => Assert.Contains($"`{icon}`", attribution, StringComparison.Ordinal));
        Assert.Contains("nine icons", attribution, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackagingScript_RequiresSelfContainedSingleFileSmallestArchiveAndUnsignedMetadata()
    {
        string root = RepositoryRoot.Find();
        string script = File.ReadAllText(Path.Combine(root, "packaging", "build-portable.ps1"));

        Assert.Contains("--self-contained", script, StringComparison.Ordinal);
        Assert.Contains("PublishSingleFile=true", script, StringComparison.Ordinal);
        Assert.Contains(
            "[System.IO.Compression.CompressionLevel]::SmallestSize",
            script,
            StringComparison.Ordinal);
        Assert.Contains("signed", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Modam Pro.ttf", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SlimPackagingScript_IsFrameworkDependentAndDeclaresDesktopRuntime()
    {
        string root = RepositoryRoot.Find();
        string scriptPath = Path.Combine(root, "packaging", "build-slim.ps1");

        Assert.True(File.Exists(scriptPath), scriptPath);
        string script = File.ReadAllText(scriptPath);
        Assert.Contains("--self-contained", script, StringComparison.Ordinal);
        Assert.Contains("false", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Microsoft.WindowsDesktop.App", script, StringComparison.Ordinal);
        Assert.Contains("runtimeRequirement", script, StringComparison.Ordinal);
        Assert.Contains("signed", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackageVerifier_AcceptsPortableAndSlimContracts()
    {
        string root = RepositoryRoot.Find();
        string verifier = File.ReadAllText(Path.Combine(root, "packaging", "verify-package.ps1"));

        Assert.Contains("selfContained", verifier, StringComparison.Ordinal);
        Assert.Contains("runtimeRequirement", verifier, StringComparison.Ordinal);
        Assert.Contains("qsearch.ico", verifier, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackageVerifier_CanPinVersionAndPublicSourceRevision()
    {
        string root = RepositoryRoot.Find();
        string verifier = File.ReadAllText(Path.Combine(root, "packaging", "verify-package.ps1"));

        Assert.Contains("[string]$ExpectedVersion", verifier, StringComparison.Ordinal);
        Assert.Contains("[string]$ExpectedRevision", verifier, StringComparison.Ordinal);
        Assert.Contains("$metadata.version -ne $ExpectedVersion", verifier, StringComparison.Ordinal);
        Assert.Contains("$metadata.commit -ne $ExpectedRevision", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagingScripts_AcceptAnExplicitPublicSourceRevision()
    {
        string root = RepositoryRoot.Find();
        foreach (string name in new[] { "build-portable.ps1", "build-slim.ps1" })
        {
            string script = File.ReadAllText(Path.Combine(root, "packaging", name));

            Assert.Contains("[string]$SourceRevision", script, StringComparison.Ordinal);
            Assert.Contains("$sourceRevision = $SourceRevision", script, StringComparison.Ordinal);
            Assert.Contains("commit = $sourceRevision.Trim()", script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReleaseWorkflow_PublishesOnlyOwnerTriggeredUnsignedPrereleases()
    {
        string root = RepositoryRoot.Find();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        string ciWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        string globalJson = File.ReadAllText(Path.Combine(root, "global.json"));

        int candidateStart = workflow.IndexOf("  unsigned-candidate:", StringComparison.Ordinal);
        int publisherStart = workflow.IndexOf("  publish-prerelease:", StringComparison.Ordinal);
        Assert.True(candidateStart >= 0, "Unsigned candidate job was not found.");
        Assert.True(publisherStart > candidateStart, "Publisher must be a separate job after candidate verification.");
        string candidate = workflow[candidateStart..publisherStart];
        string publisher = workflow[publisherStart..];

        Assert.Contains("publish_unsigned_prerelease", workflow, StringComparison.Ordinal);
        Assert.Contains("github.actor == github.repository_owner", publisher, StringComparison.Ordinal);
        Assert.Contains("github.triggering_actor == github.repository_owner", publisher, StringComparison.Ordinal);
        Assert.Contains("needs: unsigned-candidate", publisher, StringComparison.Ordinal);
        Assert.DoesNotContain("contents: write", candidate, StringComparison.Ordinal);
        Assert.Contains("contents: write", publisher, StringComparison.Ordinal);
        Assert.Contains("actions: read", publisher, StringComparison.Ordinal);
        Assert.Contains(
            "actions/download-artifact@9000827ccba6bdab643e8b6fd33ac0654aef8333",
            publisher,
            StringComparison.Ordinal);
        Assert.Contains("$PSNativeCommandUseErrorActionPreference = $true", candidate, StringComparison.Ordinal);
        Assert.Contains("overwrite: true", candidate, StringComparison.Ordinal);
        Assert.Contains("--paginate", publisher, StringComparison.Ordinal);
        Assert.Contains("--slurp", publisher, StringComparison.Ordinal);
        Assert.Contains("matching-refs/tags/$tag", publisher, StringComparison.Ordinal);
        Assert.Contains("git/refs", publisher, StringComparison.Ordinal);
        Assert.Contains("--verify-tag", publisher, StringComparison.Ordinal);
        Assert.Contains("-ceq", publisher, StringComparison.Ordinal);
        Assert.Contains("Release asset set mismatch", publisher, StringComparison.Ordinal);
        Assert.Contains("Unsigned preview", publisher, StringComparison.Ordinal);
        Assert.Contains("--prerelease", publisher, StringComparison.Ordinal);
        Assert.Contains("--draft=false", publisher, StringComparison.Ordinal);
        Assert.Contains("concurrency:", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet-version: 10.0.404", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet-version: 10.0.404", ciWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-version: 10.0.x", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-version: 10.0.x", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"10.0.404\"", globalJson, StringComparison.Ordinal);
        Assert.Contains("\"rollForward\": \"disable\"", globalJson, StringComparison.Ordinal);
        Assert.DoesNotContain("publish_public_release", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeNoticeStager_UsesThePublishedRuntimeConfigVersions()
    {
        string root = RepositoryRoot.Find();
        string stager = File.ReadAllText(Path.Combine(root, "packaging", "stage-legal-files.ps1"));
        string portable = File.ReadAllText(Path.Combine(root, "packaging", "build-portable.ps1"));

        Assert.Contains("[string]$RuntimeConfigPath", stager, StringComparison.Ordinal);
        Assert.Contains("includedFrameworks", stager, StringComparison.Ordinal);
        Assert.DoesNotContain("Find-LatestPackageDirectory", stager, StringComparison.Ordinal);
        Assert.Contains("-RuntimeConfigPath $runtimeConfig", portable, StringComparison.Ordinal);
    }

    [Fact]
    public void PortablePackage_ContainsAppHelperLicensesAndNoLicensedModamBinary()
    {
        string? packagePath = Environment.GetEnvironmentVariable("QSEARCH_TEST_PACKAGE");
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            Assert.Skip("Set QSEARCH_TEST_PACKAGE to verify a built portable ZIP.");
            return;
        }

        const long maximumPortableBytes = 100L * 1024 * 1024;
        long packageBytes = new FileInfo(packagePath).Length;
        Assert.True(
            packageBytes <= maximumPortableBytes,
            $"Portable ZIP is {packageBytes:N0} bytes; maximum is {maximumPortableBytes:N0} bytes.");

        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        string[] entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains(entries, entry => entry.EndsWith("Qsearch.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, entry => entry.EndsWith("Qsearch.ElevatedIndexer.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, entry => entry.EndsWith("LICENSE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, entry => entry.EndsWith("PRIVACY.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, entry => entry.Equals("THIRD-PARTY-NOTICES.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, entry => entry.Equals("licenses/Microsoft.Data.Sqlite/MIT.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, entry => entry.Equals("licenses/SQLitePCLRaw/Apache-2.0.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, entry => entry.Equals("licenses/FontAwesome-Free/LICENSE.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, entry => entry.Equals("licenses/FontAwesome-Free/ATTRIBUTION.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, entry => entry.Equals("licenses/dotnet-runtime/LICENSE.TXT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, entry => entry.Equals("licenses/dotnet-runtime/THIRD-PARTY-NOTICES.TXT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, entry => entry.Equals("licenses/windows-desktop-runtime/LICENSE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            entries,
            entry => entry.Contains("Modam", StringComparison.OrdinalIgnoreCase) &&
                Path.GetExtension(entry) is ".ttf" or ".otf");
    }

    [Fact]
    public void SlimPackage_ContainsRuntimeContractAndBrandIcon()
    {
        string? packagePath = Environment.GetEnvironmentVariable("QSEARCH_TEST_SLIM_PACKAGE");
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            Assert.Skip("Set QSEARCH_TEST_SLIM_PACKAGE to verify a built slim ZIP.");
            return;
        }

        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry metadataEntry = Assert.Single(
            archive.Entries,
            entry => entry.FullName.Equals("VERSION.json", StringComparison.OrdinalIgnoreCase));
        using StreamReader reader = new(metadataEntry.Open());
        string metadata = reader.ReadToEnd();

        Assert.Contains("Microsoft.WindowsDesktop.App", metadata, StringComparison.Ordinal);
        Assert.Contains("\"selfContained\": false", metadata, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            archive.Entries,
            entry => entry.FullName.Equals("qsearch.ico", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            archive.Entries,
            entry => entry.FullName.Equals("THIRD-PARTY-NOTICES.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            archive.Entries,
            entry => entry.FullName.Equals("licenses/FontAwesome-Free/ATTRIBUTION.md", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            archive.Entries,
            entry => entry.FullName.StartsWith("licenses/dotnet-runtime/", StringComparison.OrdinalIgnoreCase));
    }
}
