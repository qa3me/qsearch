using Qsearch.Safety.Models;

namespace Qsearch.Safety.Evidence;

public static class FileHeaderClassifier
{
    public static FileKind Classify(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 2 && header[0] == 0x4D && header[1] == 0x5A)
        {
            return FileKind.PortableExecutable;
        }

        if (header.StartsWith("%PDF"u8))
        {
            return FileKind.PdfDocument;
        }

        if (header.Length >= 4 &&
            header[0] == 0x50 &&
            header[1] == 0x4B &&
            header[2] == 0x03 &&
            header[3] == 0x04)
        {
            return FileKind.Archive;
        }

        if (header.StartsWith("#!"u8))
        {
            return FileKind.Script;
        }

        return FileKind.Unknown;
    }

    public static FileKind ClassifyClaimedType(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".exe" or ".dll" or ".scr" or ".com" => FileKind.PortableExecutable,
            ".ps1" or ".bat" or ".cmd" or ".vbs" or ".js" or ".wsf" => FileKind.Script,
            ".msi" => FileKind.Installer,
            ".lnk" => FileKind.Shortcut,
            ".docm" or ".xlsm" or ".pptm" => FileKind.MacroDocument,
            ".pdf" => FileKind.PdfDocument,
            ".zip" or ".7z" or ".rar" => FileKind.Archive,
            _ => FileKind.Unknown,
        };
    }
}
