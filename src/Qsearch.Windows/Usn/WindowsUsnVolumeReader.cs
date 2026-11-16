using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Qsearch.Windows.Usn;

public sealed class WindowsUsnVolumeReader : IUsnVolumeReader
{
    private const int BufferSize = 64 * 1024;

    public ValueTask<UsnJournalState> QueryJournalAsync(
        string volumeRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SafeFileHandle volume = OpenVolume(volumeRoot);
        byte[] output = new byte[56];
        if (!UsnNative.DeviceIoControl(
                volume,
                UsnNative.FsctlQueryUsnJournal,
                IntPtr.Zero,
                0,
                output,
                output.Length,
                out int bytesReturned,
                IntPtr.Zero) ||
            bytesReturned < 24)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return ValueTask.FromResult(new UsnJournalState(
            unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(0, 8))),
            BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(8, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(16, 8))));
    }

    public async IAsyncEnumerable<UsnRecord> EnumerateAsync(
        string volumeRoot,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using SafeFileHandle volume = OpenVolume(volumeRoot);
        byte[] input = new byte[24];
        BinaryPrimitives.WriteInt64LittleEndian(input.AsSpan(8, 8), 0);
        BinaryPrimitives.WriteInt64LittleEndian(input.AsSpan(16, 8), long.MaxValue);
        byte[] output = new byte[BufferSize];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool success = UsnNative.DeviceIoControl(
                volume,
                UsnNative.FsctlEnumUsnData,
                input,
                input.Length,
                output,
                output.Length,
                out int bytesReturned,
                IntPtr.Zero);
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == UsnNative.ErrorHandleEof)
                {
                    yield break;
                }

                throw new Win32Exception(error);
            }

            if (bytesReturned <= sizeof(long))
            {
                yield break;
            }

            ulong nextReference = BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(0, 8));
            BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(0, 8), nextReference);
            UsnParseResult parsed = UsnRecordParser.Parse(output.AsSpan(8, bytesReturned - 8));
            if (parsed.Diagnostics.Count > 0)
            {
                throw new UsnParseException(parsed.Diagnostics);
            }

            foreach (UsnRecord record in parsed.Records)
            {
                yield return record;
            }

            await Task.Yield();
        }
    }

    public async IAsyncEnumerable<UsnReadBatch> ReadChangesAsync(
        string volumeRoot,
        long startUsn,
        long journalId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using SafeFileHandle volume = OpenVolume(volumeRoot);
        byte[] input = new byte[40];
        BinaryPrimitives.WriteInt64LittleEndian(input.AsSpan(0, 8), startUsn);
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(8, 4), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(12, 4), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(16, 8), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(24, 8), 0);
        BinaryPrimitives.WriteInt64LittleEndian(input.AsSpan(32, 8), journalId);
        byte[] output = new byte[BufferSize];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!UsnNative.DeviceIoControl(
                    volume,
                    UsnNative.FsctlReadUsnJournal,
                    input,
                    input.Length,
                    output,
                    output.Length,
                    out int bytesReturned,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (bytesReturned < sizeof(long))
            {
                throw new InvalidDataException("The NTFS journal response did not contain a next USN value.");
            }

            long nextUsn = BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(0, 8));
            BinaryPrimitives.WriteInt64LittleEndian(input.AsSpan(0, 8), nextUsn);
            UsnParseResult parsed = bytesReturned == sizeof(long)
                ? new UsnParseResult([], [])
                : UsnRecordParser.Parse(output.AsSpan(8, bytesReturned - 8));
            yield return new UsnReadBatch(parsed.Records, nextUsn, parsed.Diagnostics);

            if (parsed.Records.Count == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }
    }

    private static SafeFileHandle OpenVolume(string volumeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeRoot);
        string root = Path.GetPathRoot(volumeRoot) ?? volumeRoot;
        string devicePath = @"\\.\" + root.TrimEnd('\\', '/');
        SafeFileHandle handle = UsnNative.CreateFileW(
            devicePath,
            UsnNative.GenericRead,
            UsnNative.FileShareRead | UsnNative.FileShareWrite | UsnNative.FileShareDelete,
            IntPtr.Zero,
            UsnNative.OpenExisting,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        return handle;
    }
}
