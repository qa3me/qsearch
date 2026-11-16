using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Qsearch.Windows.Elevation;

public sealed class ElevatedIndexerLauncher : IElevatedIndexerClient
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);
    private readonly string _helperPath;

    public ElevatedIndexerLauncher(string helperPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(helperPath);
        _helperPath = Path.GetFullPath(helperPath);
    }

    public async ValueTask<IElevatedIndexerSession> ConnectAsync(
        VolumeConsent consent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consent);
        if (consent.AllowedVolumes.Count == 0)
        {
            throw new ArgumentException("At least one volume must be explicitly consented.", nameof(consent));
        }

        string pipeName = "qsearch-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string volumes = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(consent.AllowedVolumes)));
        ProcessStartInfo startInfo = new(_helperPath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--nonce");
        startInfo.ArgumentList.Add(nonce);
        startInfo.ArgumentList.Add("--sid");
        startInfo.ArgumentList.Add(consent.UserSid);
        startInfo.ArgumentList.Add("--volumes");
        startInfo.ArgumentList.Add(volumes);

        Process process;
        try
        {
            process = Process.Start(startInfo) ??
                throw new InvalidOperationException("The elevated index helper could not be started.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new ElevationDeclinedException(exception);
        }

        using (process)
        {
            NamedPipeClientStream pipe = new(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            try
            {
                using CancellationTokenSource timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ConnectionTimeout);
                await pipe.ConnectAsync(timeout.Token);
                ElevatedIndexerClient client = new(pipe);
                HelperResponse response = await client.SendAsync(
                    ElevatedIndexerMessageTypes.Hello,
                    new HelloMessage(consent.UserSid, nonce),
                    timeout.Token);
                if (!response.Success)
                {
                    await client.DisposeAsync();
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "The elevated helper rejected authentication ({0}).",
                            response.ErrorCode));
                }

                return client;
            }
            catch
            {
                await pipe.DisposeAsync();
                throw;
            }
        }
    }
}

public sealed class ElevationDeclinedException : OperationCanceledException
{
    public ElevationDeclinedException()
        : base("The Windows elevation prompt was declined.")
    {
    }

    public ElevationDeclinedException(Exception innerException)
        : base("The Windows elevation prompt was declined.", innerException)
    {
    }
}
