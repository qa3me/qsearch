using System.ComponentModel;
using System.Diagnostics;

namespace Qsearch.Windows.Shell;

public interface IShellActionService
{
    ValueTask OpenAsync(string path, CancellationToken cancellationToken);

    ValueTask RevealAsync(string path, CancellationToken cancellationToken);
}

public interface IShellProcessLauncher
{
    void Start(ProcessStartInfo startInfo);
}

public sealed class ShellActionService : IShellActionService
{
    private readonly IShellProcessLauncher _launcher;

    public ShellActionService(IShellProcessLauncher? launcher = null)
    {
        _launcher = launcher ?? new ShellProcessLauncher();
    }

    public async ValueTask OpenAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        ProcessStartInfo startInfo = new(path)
        {
            UseShellExecute = true,
        };
        try
        {
            await RunOnStaThreadAsync(() => _launcher.Start(startInfo), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1155)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessStartInfo chooser = new("rundll32.exe")
            {
                UseShellExecute = false,
            };
            chooser.ArgumentList.Add("shell32.dll,OpenAs_RunDLL");
            chooser.ArgumentList.Add(path);
            await RunOnStaThreadAsync(() => _launcher.Start(chooser), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask RevealAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        ProcessStartInfo startInfo = new("explorer.exe")
        {
            UseShellExecute = true,
            Arguments = $"/select,\"{path}\"",
        };
        await RunOnStaThreadAsync(() => _launcher.Start(startInfo), cancellationToken)
            .ConfigureAwait(false);
    }

    private static ValueTask RunOnStaThreadAsync(
        Action action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows shell actions require Windows.");
        }

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                completion.TrySetResult();
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Qsearch shell action",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return new ValueTask(completion.Task);
    }
}

public sealed class ShellProcessLauncher : IShellProcessLauncher
{
    public void Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        Process.Start(startInfo)?.Dispose();
    }
}
