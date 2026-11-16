using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Qsearch.App.Services;

public enum FilePurposeCategory
{
    Unknown,
    Folder,
    Document,
    Image,
    AudioVideo,
    Archive,
    Script,
    Installer,
    Shortcut,
    Application,
    Library,
}

public sealed record FilePurposeSummary(
    FilePurposeCategory Category,
    string Summary,
    string Requirement,
    string? PublisherLabel = null,
    string? Architecture = null);

public static class FilePurposeClassifier
{
    private const int MaximumPeProbeBytes = 4096;

    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv", ".doc", ".docx", ".epub", ".json", ".md", ".odt", ".pdf",
        ".ppt", ".pptx", ".rtf", ".txt", ".xls", ".xlsx", ".xml",
    };

    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".gif", ".heic", ".ico", ".jpeg", ".jpg", ".png", ".svg", ".tif", ".tiff", ".webp",
    };

    private static readonly HashSet<string> Media = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aac", ".avi", ".flac", ".m4a", ".mkv", ".mov", ".mp3", ".mp4", ".ogg", ".wav", ".webm", ".wmv",
    };

    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".bz2", ".gz", ".rar", ".tar", ".xz", ".zip",
    };

    private static readonly HashSet<string> Scripts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cmd", ".js", ".ps1", ".py", ".vbs", ".wsf",
    };

    public static FilePurposeSummary Classify(string path, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(culture);
        string fullPath = Path.GetFullPath(path);
        bool persian = string.Equals(culture.TwoLetterISOLanguageName, "fa", StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(fullPath))
        {
            return persian
                ? new(FilePurposeCategory.Folder, "این مورد یک پوشه برای نگهداری و دسته‌بندی فایل‌هاست.", "برای مرور آن به فایل اکسپلورر یا Qsearch نیاز است.")
                : new(FilePurposeCategory.Folder, "This item is a folder used to organize files.", "Open it with File Explorer or Qsearch.");
        }

        string extension = Path.GetExtension(fullPath);
        if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return DescribePortableExecutable(fullPath, extension, persian);
        }

        FilePurposeCategory category = extension.ToLowerInvariant() switch
        {
            ".appref-ms" => FilePurposeCategory.Shortcut,
            ".lnk" => FilePurposeCategory.Shortcut,
            ".msi" => FilePurposeCategory.Installer,
            ".msix" => FilePurposeCategory.Installer,
            ".msixbundle" => FilePurposeCategory.Installer,
            _ when Documents.Contains(extension) => FilePurposeCategory.Document,
            _ when Images.Contains(extension) => FilePurposeCategory.Image,
            _ when Media.Contains(extension) => FilePurposeCategory.AudioVideo,
            _ when Archives.Contains(extension) => FilePurposeCategory.Archive,
            _ when Scripts.Contains(extension) => FilePurposeCategory.Script,
            _ => FilePurposeCategory.Unknown,
        };
        return DescribeCategory(category, persian);
    }

    private static FilePurposeSummary DescribePortableExecutable(
        string fullPath,
        string extension,
        bool persian)
    {
        PortableExecutableInfo pe = ProbePortableExecutable(fullPath);
        bool library = pe.IsDll || string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase);
        FilePurposeCategory category = library ? FilePurposeCategory.Library : FilePurposeCategory.Application;
        string? publisherLabel = ReadPublisherLabel(fullPath);
        string architecture = pe.Architecture ?? (persian ? "نامشخص" : "unknown architecture");
        if (persian)
        {
            string summary = library
                ? "این فایل یک کتابخانهٔ برنامه است و معمولاً به‌تنهایی اجرا نمی‌شود."
                : "این فایل یک برنامهٔ ویندوز است؛ کار دقیقش فقط از ساختار فایل قابل تشخیص نیست.";
            string requirement = pe.IsManaged
                ? $"برای اجرا به ویندوز و نسخهٔ سازگار ‎.NET نیاز دارد ({architecture})."
                : library
                    ? $"باید توسط یک برنامهٔ سازگار ویندوز بارگذاری شود ({architecture})."
                    : $"برای اجرا به ویندوز سازگار نیاز دارد ({architecture}).";
            return new(category, summary, requirement, publisherLabel, architecture);
        }

        string englishSummary = library
            ? "This is an application library and normally does not run by itself."
            : "This is a Windows application; its exact behavior cannot be inferred from structure alone.";
        string englishRequirement = pe.IsManaged
            ? $"Requires Windows and a compatible .NET runtime ({architecture})."
            : library
                ? $"Loaded by a compatible Windows application ({architecture})."
                : $"Requires a compatible Windows system ({architecture}).";
        return new(category, englishSummary, englishRequirement, publisherLabel, architecture);
    }

    private static FilePurposeSummary DescribeCategory(FilePurposeCategory category, bool persian)
    {
        if (persian)
        {
            return category switch
            {
                FilePurposeCategory.Document => new(category, "این فایل برای نگهداری متن یا دادهٔ سندی است.", "به یک برنامهٔ سازگار با قالب فایل نیاز دارد."),
                FilePurposeCategory.Image => new(category, "این فایل یک تصویر است.", "به نمایشگر تصویر سازگار نیاز دارد."),
                FilePurposeCategory.AudioVideo => new(category, "این فایل محتوای صوتی یا تصویری است.", "به پخش‌کننده‌ای با کُدک سازگار نیاز دارد."),
                FilePurposeCategory.Archive => new(category, "این فایل چند مورد را به‌صورت فشرده نگه می‌دارد.", "برای دیدن محتوا به ابزار بازکردن آرشیو نیاز دارد."),
                FilePurposeCategory.Script => new(category, "این فایل شامل دستورهایی برای یک مفسر اسکریپت است و ممکن است روی سیستم تغییر ایجاد کند.", "فقط با مفسر مربوط و پس از بررسی متن اجرا شود."),
                FilePurposeCategory.Installer => new(category, "این فایل برای نصب یا تغییر یک برنامهٔ ویندوز ساخته شده است.", "به Windows Installer یا App Installer نیاز دارد."),
                FilePurposeCategory.Shortcut => new(category, "این فایل یک میان‌بر به برنامه، فایل یا فرمان دیگری است.", "برای تشخیص مقصد باید اطلاعات میان‌بر بررسی شود."),
                _ => new(category, "کاربرد این قالب از روی پسوند و ساختار محدود فایل مشخص نشد.", "برای بازکردن به برنامه‌ای نیاز است که این قالب را بشناسد."),
            };
        }

        return category switch
        {
            FilePurposeCategory.Document => new(category, "This file stores document text or structured data.", "Requires an application compatible with this format."),
            FilePurposeCategory.Image => new(category, "This file contains an image.", "Requires a compatible image viewer."),
            FilePurposeCategory.AudioVideo => new(category, "This file contains audio or video media.", "Requires a player with a compatible codec."),
            FilePurposeCategory.Archive => new(category, "This file stores one or more compressed items.", "Requires an archive tool to inspect its contents."),
            FilePurposeCategory.Script => new(category, "This file contains instructions for a script interpreter and may change the system when run.", "Run only with its matching interpreter after reviewing the text."),
            FilePurposeCategory.Installer => new(category, "This file is intended to install or modify a Windows application.", "Requires Windows Installer or App Installer."),
            FilePurposeCategory.Shortcut => new(category, "This file points to another application, file, or command.", "Inspect the shortcut metadata to identify its target."),
            _ => new(category, "The purpose of this format was not identified from its extension and bounded structure.", "Requires an application that recognizes this format."),
        };
    }

    private static PortableExecutableInfo ProbePortableExecutable(string path)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 512,
                FileOptions.SequentialScan);
            Span<byte> dosHeader = stackalloc byte[64];
            if (stream.Read(dosHeader) != dosHeader.Length || dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z')
            {
                return default;
            }

            int peOffset = BinaryPrimitives.ReadInt32LittleEndian(dosHeader[0x3c..]);
            if (peOffset < 64 || peOffset > stream.Length - 24)
            {
                return default;
            }

            stream.Position = peOffset;
            int length = checked((int)Math.Min(MaximumPeProbeBytes, stream.Length - peOffset));
            byte[] header = new byte[length];
            int read = stream.Read(header);
            ReadOnlySpan<byte> bytes = header.AsSpan(0, read);
            if (bytes.Length < 26 || bytes[0] != (byte)'P' || bytes[1] != (byte)'E' || bytes[2] != 0 || bytes[3] != 0)
            {
                return default;
            }

            ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
            ushort optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..]);
            ushort characteristics = BinaryPrimitives.ReadUInt16LittleEndian(bytes[22..]);
            bool isDll = (characteristics & 0x2000) != 0;
            bool managed = false;
            if (optionalSize >= 112 && bytes.Length >= 26)
            {
                ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes[24..]);
                int dataDirectories = magic switch
                {
                    0x10b => 24 + 96,
                    0x20b => 24 + 112,
                    _ => -1,
                };
                int clrDirectory = dataDirectories + (14 * 8);
                if (dataDirectories >= 0 && bytes.Length >= clrDirectory + 8)
                {
                    managed = BinaryPrimitives.ReadUInt32LittleEndian(bytes[clrDirectory..]) != 0;
                }
            }

            return new PortableExecutableInfo(true, isDll, managed, MachineName(machine));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return default;
        }
    }

    private static string? ReadPublisherLabel(string path)
    {
        try
        {
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(version.FileDescription)
                ? null
                : version.FileDescription.Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string MachineName(ushort machine) => machine switch
    {
        0x014c => "x86",
        0x8664 => "x64",
        0xaa64 => "ARM64",
        0x01c4 => "ARM",
        _ => "unknown architecture",
    };

    private readonly record struct PortableExecutableInfo(
        bool IsPortableExecutable,
        bool IsDll,
        bool IsManaged,
        string? Architecture);
}
