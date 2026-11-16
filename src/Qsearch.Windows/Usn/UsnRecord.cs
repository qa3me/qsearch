using System.Globalization;
using Qsearch.Core.Indexing;

namespace Qsearch.Windows.Usn;

public readonly record struct FileReferenceId(ulong Low, ulong High = 0)
{
    public static FileReferenceId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Length switch
        {
            16 => new FileReferenceId(
                ulong.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
            32 => new FileReferenceId(
                ulong.Parse(value.AsSpan(16, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
                ulong.Parse(value.AsSpan(0, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
            _ => throw new FormatException("The file reference identity must contain 16 or 32 hexadecimal characters."),
        };
    }

    public override string ToString()
    {
        return High == 0
            ? Low.ToString("x16", CultureInfo.InvariantCulture)
            : High.ToString("x16", CultureInfo.InvariantCulture) +
                Low.ToString("x16", CultureInfo.InvariantCulture);
    }
}

[Flags]
public enum UsnReason : uint
{
    None = 0,
    DataOverwrite = 0x00000001,
    DataExtend = 0x00000002,
    DataTruncation = 0x00000004,
    NamedDataOverwrite = 0x00000010,
    FileCreate = 0x00000100,
    FileDelete = 0x00000200,
    RenameOldName = 0x00001000,
    RenameNewName = 0x00002000,
    BasicInfoChange = 0x00008000,
    Close = 0x80000000,
}

public sealed record UsnRecord(
    FileReferenceId FileReferenceNumber,
    FileReferenceId ParentFileReferenceNumber,
    long Usn,
    UsnReason Reason,
    FileAttributes Attributes,
    string Name,
    ushort MajorVersion);

public sealed record UsnParseResult(
    IReadOnlyList<UsnRecord> Records,
    IReadOnlyList<IndexDiagnostic> Diagnostics);

public sealed class UsnParseException : IOException
{
    public UsnParseException(IReadOnlyList<IndexDiagnostic> diagnostics)
        : base("The NTFS journal returned malformed records.")
    {
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<IndexDiagnostic> Diagnostics { get; }
}
