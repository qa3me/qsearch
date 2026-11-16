using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Qsearch.App.Services;

public sealed record FileAssociationInfo(
    string HandlerName,
    ImageSource? HandlerIcon,
    bool IsAvailable)
{
    public static FileAssociationInfo Unavailable { get; } = new("No default app", null, false);
}

public interface IFileAssociationService
{
    ValueTask<FileAssociationInfo> ResolveAsync(string path, CancellationToken cancellationToken);
}

public interface IWindowsFileAssociationApi
{
    FileAssociationInfo Resolve(string fullPath);
}

public sealed class WindowsFileAssociationService : IFileAssociationService
{
    private readonly IWindowsFileAssociationApi _nativeApi;

    public WindowsFileAssociationService(IWindowsFileAssociationApi? nativeApi = null)
    {
        _nativeApi = nativeApi ?? new WindowsFileAssociationApi();
    }

    public async ValueTask<FileAssociationInfo> ResolveAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(path);
        return await Task.Run(
            () => _nativeApi.Resolve(fullPath),
            cancellationToken).ConfigureAwait(false);
    }
}

public sealed class WindowsFileAssociationApi : IWindowsFileAssociationApi
{
    private const uint AssocfInitIgnoreUnknown = 0x00000400;
    private const uint AssocstrExecutable = 2;
    private const uint AssocstrFriendlyAppName = 4;
    private const uint ShgfiIcon = 0x00000100;
    private const uint ShgfiSmallIcon = 0x00000001;
    private const uint ShgfiUseFileAttributes = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;

    public FileAssociationInfo Resolve(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        bool isDirectory = Directory.Exists(fullPath);
        string association = isDirectory ? "Folder" : Path.GetExtension(fullPath);
        string? friendlyName = isDirectory
            ? "File Explorer"
            : QueryAssociation(association, AssocstrFriendlyAppName);
        string? executable = isDirectory
            ? Environment.ExpandEnvironmentVariables(@"%WINDIR%\explorer.exe")
            : QueryAssociation(association, AssocstrExecutable);
        if (string.IsNullOrWhiteSpace(friendlyName) && !string.IsNullOrWhiteSpace(executable))
        {
            friendlyName = ReadExecutableLabel(executable);
        }

        ImageSource? icon = ReadSmallIcon(fullPath, isDirectory);
        bool available = !string.IsNullOrWhiteSpace(friendlyName) || !string.IsNullOrWhiteSpace(executable);
        return new FileAssociationInfo(
            friendlyName ?? (available ? "Windows application" : FileAssociationInfo.Unavailable.HandlerName),
            icon,
            available);
    }

    private static string? QueryAssociation(string association, uint query)
    {
        if (string.IsNullOrWhiteSpace(association))
        {
            return null;
        }

        uint length = 0;
        _ = NativeMethods.AssocQueryString(
            AssocfInitIgnoreUnknown,
            query,
            association,
            null,
            null,
            ref length);
        if (length is 0 or > 32768)
        {
            return null;
        }

        char[] output = new char[checked((int)length)];
        uint actualLength = length;
        int result = NativeMethods.AssocQueryString(
            AssocfInitIgnoreUnknown,
            query,
            association,
            null,
            output,
            ref actualLength);
        int textLength = checked((int)Math.Min(actualLength, (uint)output.Length));
        if (textLength > 0 && output[textLength - 1] == '\0')
        {
            textLength--;
        }

        return result == 0 && textLength > 0 ? new string(output, 0, textLength) : null;
    }

    private static string ReadExecutableLabel(string executable)
    {
        try
        {
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(executable);
            if (!string.IsNullOrWhiteSpace(version.FileDescription))
            {
                return version.FileDescription.Trim();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return Path.GetFileNameWithoutExtension(executable);
    }

    private static BitmapSource? ReadSmallIcon(string fullPath, bool isDirectory)
    {
        uint attributes = isDirectory ? (uint)FileAttributes.Directory : FileAttributeNormal;
        uint flags = ShgfiIcon | ShgfiSmallIcon;
        if (!File.Exists(fullPath) && !isDirectory)
        {
            flags |= ShgfiUseFileAttributes;
        }

        IntPtr result = NativeMethods.SHGetFileInfo(
            fullPath,
            attributes,
            out ShellFileInfo info,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            flags);
        if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                info.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(24, 24));
            source.Freeze();
            return source;
        }
        finally
        {
            _ = NativeMethods.DestroyIcon(info.IconHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    private static class NativeMethods
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = false)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int AssocQueryString(
            uint flags,
            uint query,
            string association,
            string? extra,
            [Out] char[]? output,
            ref uint outputLength);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = false)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr SHGetFileInfo(
            string path,
            uint fileAttributes,
            out ShellFileInfo fileInfo,
            uint fileInfoSize,
            uint flags);

        [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr iconHandle);
    }
}
