using System.Runtime.InteropServices;

namespace Qsearch.Safety.Evidence;

public enum AuthenticodeState
{
    Valid,
    NoEmbeddedSignature,
    IntegrityFailure,
    TimedOut,
}

public sealed record AuthenticodeResult(AuthenticodeState State, int? NativeStatus);

public interface IAuthenticodeVerifier
{
    ValueTask<AuthenticodeResult> VerifyAsync(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed class AuthenticodeVerifier : IAuthenticodeVerifier
{
    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionIgnore = 0;
    private const uint WtdCacheOnlyUrlRetrieval = 0x00001000;
    private static readonly Guid GenericVerifyV2 = new(
        "00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public async ValueTask<AuthenticodeResult> VerifyAsync(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            int status = await Task.Run(
                    () => VerifyNative(path),
                    CancellationToken.None)
                .WaitAsync(timeout, cancellationToken);
            return MapStatus(status);
        }
        catch (TimeoutException)
        {
            return new AuthenticodeResult(AuthenticodeState.TimedOut, null);
        }
    }

    public static AuthenticodeResult MapStatus(int status)
    {
        return status switch
        {
            0 => new AuthenticodeResult(AuthenticodeState.Valid, status),
            TrustENoSignature => new AuthenticodeResult(AuthenticodeState.NoEmbeddedSignature, status),
            _ => new AuthenticodeResult(AuthenticodeState.IntegrityFailure, status),
        };
    }

    private static int VerifyNative(string path)
    {
        IntPtr pathPointer = Marshal.StringToCoTaskMemUni(path);
        IntPtr fileInfoPointer = IntPtr.Zero;
        IntPtr trustDataPointer = IntPtr.Zero;
        try
        {
            WinTrustFileInfo fileInfo = new()
            {
                StructSize = checked((uint)Marshal.SizeOf<WinTrustFileInfo>()),
                FilePath = pathPointer,
            };
            fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

            WinTrustData trustData = new()
            {
                StructSize = checked((uint)Marshal.SizeOf<WinTrustData>()),
                UiChoice = WtdUiNone,
                RevocationChecks = WtdRevokeNone,
                UnionChoice = WtdChoiceFile,
                FileInfo = fileInfoPointer,
                StateAction = WtdStateActionIgnore,
                ProviderFlags = WtdCacheOnlyUrlRetrieval,
            };
            trustDataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(trustData, trustDataPointer, false);
            Guid action = GenericVerifyV2;
            return WinVerifyTrust(IntPtr.Zero, ref action, trustDataPointer);
        }
        finally
        {
            if (trustDataPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(trustDataPointer);
            }

            if (fileInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fileInfoPointer);
            }

            Marshal.FreeCoTaskMem(pathPointer);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        ref Guid actionId,
        IntPtr trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
