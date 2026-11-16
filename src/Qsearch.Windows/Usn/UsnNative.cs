using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Qsearch.Windows.Usn;

internal static class UsnNative
{
    public const uint FsctlEnumUsnData = 0x000900B3;
    public const uint FsctlReadUsnJournal = 0x000900BB;
    public const uint FsctlQueryUsnJournal = 0x000900F4;
    public const int ErrorHandleEof = 38;
    public const uint GenericRead = 0x80000000;
    public const uint FileShareRead = 0x00000001;
    public const uint FileShareWrite = 0x00000002;
    public const uint FileShareDelete = 0x00000004;
    public const uint OpenExisting = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        int nInBufferSize,
        byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        int nInBufferSize,
        byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);
}
