using System.Buffers.Binary;
using System.Text;

namespace Qsearch.Windows.Tests.Fixtures;

internal static class UsnBufferBuilder
{
    public static byte[] WithV2Record(
        string name,
        uint? recordLength = null,
        ushort majorVersion = 2,
        ulong fileReference = 10,
        ulong parentReference = 5,
        long usn = 42,
        uint reason = 0x00000100,
        FileAttributes attributes = FileAttributes.Normal)
    {
        byte[] nameBytes = Encoding.Unicode.GetBytes(name);
        int actualLength = Align(60 + nameBytes.Length);
        byte[] buffer = new byte[actualLength];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), recordLength ?? (uint)actualLength);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), majorVersion);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8, 8), fileReference);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(16, 8), parentReference);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(24, 8), usn);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(40, 4), reason);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(52, 4), (uint)attributes);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(56, 2), checked((ushort)nameBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(58, 2), 60);
        nameBytes.CopyTo(buffer, 60);
        return buffer;
    }

    public static byte[] WithV3Record(
        string name,
        ulong fileReferenceLow = 10,
        ulong fileReferenceHigh = 11,
        ulong parentReferenceLow = 5,
        ulong parentReferenceHigh = 6)
    {
        byte[] nameBytes = Encoding.Unicode.GetBytes(name);
        int actualLength = Align(76 + nameBytes.Length);
        byte[] buffer = new byte[actualLength];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), (uint)actualLength);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), 3);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8, 8), fileReferenceLow);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(16, 8), fileReferenceHigh);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(24, 8), parentReferenceLow);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(32, 8), parentReferenceHigh);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(40, 8), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(56, 4), 0x00000100);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(68, 4), (uint)FileAttributes.Normal);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(72, 2), checked((ushort)nameBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(74, 2), 76);
        nameBytes.CopyTo(buffer, 76);
        return buffer;
    }

    private static int Align(int length)
    {
        return (length + 7) & ~7;
    }
}
