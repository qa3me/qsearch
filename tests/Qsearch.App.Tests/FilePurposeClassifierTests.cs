using System.Globalization;
using Qsearch.App.Services;

namespace Qsearch.App.Tests;

public sealed class FilePurposeClassifierTests
{
    [Theory]
    [InlineData("report.pdf", FilePurposeCategory.Document)]
    [InlineData("script.PS1", FilePurposeCategory.Script)]
    [InlineData("setup.msi", FilePurposeCategory.Installer)]
    [InlineData("shortcut.lnk", FilePurposeCategory.Shortcut)]
    [InlineData("archive.zip", FilePurposeCategory.Archive)]
    [InlineData("unknown.qz", FilePurposeCategory.Unknown)]
    public void Classify_DescribesCommonFilesWithoutExecutingThem(
        string name,
        FilePurposeCategory expected)
    {
        using TestItem item = TestItem.File(name, []);

        FilePurposeSummary summary = FilePurposeClassifier.Classify(
            item.Path,
            CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(expected, summary.Category);
        Assert.False(string.IsNullOrWhiteSpace(summary.Summary));
        Assert.False(string.IsNullOrWhiteSpace(summary.Requirement));
    }

    [Fact]
    public void Classify_DistinguishesNativeManagedAndLibraryPortableExecutables()
    {
        using TestItem native = TestItem.File("native.exe", PortableExecutable(dll: false, managed: false));
        using TestItem managed = TestItem.File("managed.exe", PortableExecutable(dll: false, managed: true));
        using TestItem library = TestItem.File("library.dll", PortableExecutable(dll: true, managed: false));

        FilePurposeSummary nativeSummary = FilePurposeClassifier.Classify(native.Path, CultureInfo.InvariantCulture);
        FilePurposeSummary managedSummary = FilePurposeClassifier.Classify(managed.Path, CultureInfo.InvariantCulture);
        FilePurposeSummary librarySummary = FilePurposeClassifier.Classify(library.Path, CultureInfo.InvariantCulture);

        Assert.Equal(FilePurposeCategory.Application, nativeSummary.Category);
        Assert.Contains("Windows", nativeSummary.Requirement, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".NET", managedSummary.Requirement, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FilePurposeCategory.Library, librarySummary.Category);
    }

    [Fact]
    public void Classify_ExplainsFoldersInPersian()
    {
        using TestItem folder = TestItem.Folder("Archive");

        FilePurposeSummary summary = FilePurposeClassifier.Classify(
            folder.Path,
            CultureInfo.GetCultureInfo("fa-IR"));

        Assert.Equal(FilePurposeCategory.Folder, summary.Category);
        Assert.Contains("پوشه", summary.Summary, StringComparison.Ordinal);
    }

    private static byte[] PortableExecutable(bool dll, bool managed)
    {
        byte[] bytes = new byte[512];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3c);
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';
        BitConverter.GetBytes((ushort)0x8664).CopyTo(bytes, 0x84);
        BitConverter.GetBytes((ushort)0xF0).CopyTo(bytes, 0x94);
        BitConverter.GetBytes((ushort)(dll ? 0x2000 : 0x0002)).CopyTo(bytes, 0x96);
        BitConverter.GetBytes((ushort)0x20b).CopyTo(bytes, 0x98);
        BitConverter.GetBytes(16).CopyTo(bytes, 0x98 + 108);
        if (managed)
        {
            BitConverter.GetBytes(0x2000).CopyTo(bytes, 0x98 + 112 + (14 * 8));
            BitConverter.GetBytes(0x48).CopyTo(bytes, 0x98 + 112 + (14 * 8) + 4);
        }

        return bytes;
    }

    private sealed class TestItem : IDisposable
    {
        private TestItem(string path, bool directory)
        {
            Path = path;
            IsDirectory = directory;
        }

        public string Path { get; }
        private bool IsDirectory { get; }

        public static TestItem File(string name, byte[] content)
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Qsearch.Purpose.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = System.IO.Path.Combine(root, name);
            System.IO.File.WriteAllBytes(path, content);
            return new TestItem(path, false);
        }

        public static TestItem Folder(string name)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Qsearch.Purpose.Tests", Guid.NewGuid().ToString("N"), name);
            Directory.CreateDirectory(path);
            return new TestItem(path, true);
        }

        public void Dispose()
        {
            string root = IsDirectory ? Directory.GetParent(Path)!.FullName : System.IO.Path.GetDirectoryName(Path)!;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
