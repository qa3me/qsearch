using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Qsearch.Windows.Elevation;
using Qsearch.Windows.Usn;

namespace Qsearch.ElevatedIndexer;

internal static class Program
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    public static async Task<int> Main(string[] args)
    {
        Dictionary<string, string> options;
        try
        {
            options = ParseOptions(args);
        }
        catch (ArgumentException)
        {
            return 2;
        }

        if (!options.TryGetValue("pipe", out string? pipeName) ||
            !options.TryGetValue("nonce", out string? nonce) ||
            !options.TryGetValue("sid", out string? sid) ||
            !options.TryGetValue("volumes", out string? encodedVolumes))
        {
            return 2;
        }

        HashSet<string>? allowedVolumes;
        try
        {
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedVolumes));
            allowedVolumes = JsonSerializer.Deserialize<HashSet<string>>(json);
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException)
        {
            return 2;
        }

        if (allowedVolumes is null || allowedVolumes.Count == 0)
        {
            return 2;
        }

        HelperCommandDispatcher dispatcher = new(
            sid,
            nonce,
            allowedVolumes,
            new WindowsUsnVolumeReader(),
            TimeProvider.System);
        await using NamedPipeServerStream pipe = new(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            ElevatedIndexerProtocol.MaximumMessageLength,
            ElevatedIndexerProtocol.MaximumMessageLength);

        using CancellationTokenSource connectionTimeout = new(IdleTimeout);
        try
        {
            await pipe.WaitForConnectionAsync(connectionTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            return 3;
        }

        while (pipe.IsConnected && !dispatcher.ShouldExit)
        {
            using CancellationTokenSource idleTimeout = new(IdleTimeout);
            try
            {
                ElevatedIndexerMessage request = await ElevatedIndexerProtocol.ReadAsync(
                    pipe,
                    idleTimeout.Token);
                HelperResponse response = await dispatcher.DispatchAsync(request, idleTimeout.Token);
                await ElevatedIndexerProtocol.WriteAsync(pipe, response, idleTimeout.Token);
            }
            catch (EndOfStreamException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return 0;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        if ((args.Length & 1) != 0)
        {
            throw new ArgumentException("Helper options must use name/value pairs.", nameof(args));
        }

        Dictionary<string, string> options = new(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            string name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) || name.Length == 2)
            {
                throw new ArgumentException("A helper option name was invalid.", nameof(args));
            }

            options[name[2..]] = args[index + 1];
        }

        return options;
    }
}
