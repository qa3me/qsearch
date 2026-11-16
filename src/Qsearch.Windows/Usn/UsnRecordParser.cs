using System.Buffers.Binary;
using System.Text;
using Qsearch.Core.Indexing;

namespace Qsearch.Windows.Usn;

public static class UsnRecordParser
{
    private const int CommonHeaderLength = 8;
    private const int VersionTwoHeaderLength = 60;
    private const int VersionThreeHeaderLength = 76;

    public static UsnParseResult Parse(ReadOnlySpan<byte> buffer)
    {
        List<UsnRecord> records = [];
        List<IndexDiagnostic> diagnostics = [];
        int position = 0;

        while (position < buffer.Length)
        {
            int remaining = buffer.Length - position;
            if (remaining < CommonHeaderLength)
            {
                diagnostics.Add(Diagnostic("truncated-record", position));
                break;
            }

            ReadOnlySpan<byte> remainingBuffer = buffer[position..];
            uint rawLength = BinaryPrimitives.ReadUInt32LittleEndian(remainingBuffer[..4]);
            if (rawLength < CommonHeaderLength || rawLength > remaining)
            {
                diagnostics.Add(Diagnostic("invalid-record-length", position));
                break;
            }

            int recordLength = checked((int)rawLength);
            ReadOnlySpan<byte> recordBuffer = remainingBuffer[..recordLength];
            ushort majorVersion = BinaryPrimitives.ReadUInt16LittleEndian(recordBuffer.Slice(4, 2));
            UsnRecord? record = majorVersion switch
            {
                2 => ParseVersionTwo(recordBuffer, position, diagnostics),
                3 => ParseVersionThree(recordBuffer, position, diagnostics),
                _ => null,
            };

            if (majorVersion is not 2 and not 3)
            {
                diagnostics.Add(Diagnostic("unsupported-record-version", position));
            }
            else if (record is not null)
            {
                records.Add(record);
            }

            position += recordLength;
        }

        return new UsnParseResult(records, diagnostics);
    }

    private static UsnRecord? ParseVersionTwo(
        ReadOnlySpan<byte> buffer,
        int position,
        List<IndexDiagnostic> diagnostics)
    {
        if (buffer.Length < VersionTwoHeaderLength ||
            !TryReadName(buffer, VersionTwoHeaderLength, 56, 58, out string? name))
        {
            diagnostics.Add(Diagnostic("invalid-record-offset", position));
            return null;
        }

        return new UsnRecord(
            new FileReferenceId(BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(8, 8))),
            new FileReferenceId(BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(16, 8))),
            BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(24, 8)),
            (UsnReason)BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(40, 4)),
            (FileAttributes)BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(52, 4)),
            name,
            2);
    }

    private static UsnRecord? ParseVersionThree(
        ReadOnlySpan<byte> buffer,
        int position,
        List<IndexDiagnostic> diagnostics)
    {
        if (buffer.Length < VersionThreeHeaderLength ||
            !TryReadName(buffer, VersionThreeHeaderLength, 72, 74, out string? name))
        {
            diagnostics.Add(Diagnostic("invalid-record-offset", position));
            return null;
        }

        return new UsnRecord(
            new FileReferenceId(
                BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(8, 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(16, 8))),
            new FileReferenceId(
                BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(24, 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(32, 8))),
            BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(40, 8)),
            (UsnReason)BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(56, 4)),
            (FileAttributes)BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(68, 4)),
            name,
            3);
    }

    private static bool TryReadName(
        ReadOnlySpan<byte> buffer,
        int minimumOffset,
        int lengthFieldOffset,
        int offsetFieldOffset,
        out string name)
    {
        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(
            buffer.Slice(lengthFieldOffset, 2));
        ushort nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(
            buffer.Slice(offsetFieldOffset, 2));
        if ((nameLength & 1) != 0 ||
            nameOffset < minimumOffset ||
            nameOffset > buffer.Length ||
            nameLength > buffer.Length - nameOffset)
        {
            name = string.Empty;
            return false;
        }

        name = Encoding.Unicode.GetString(buffer.Slice(nameOffset, nameLength));
        return true;
    }

    private static IndexDiagnostic Diagnostic(string code, int position)
    {
        return new IndexDiagnostic(
            code,
            $"The journal record at byte offset {position} was rejected.");
    }
}
