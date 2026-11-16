using System.ComponentModel;
using System.Diagnostics;
using Qsearch.Windows.Shell;

namespace Qsearch.Windows.Tests;

public sealed class ShellActionServiceTests
{
    [Fact]
    public async Task OpenAsync_UsesANonBlockingStaShellThread()
    {
        using BlockingLauncher launcher = new();
        ShellActionService service = new(launcher);

        ValueTask operation = service.OpenAsync(
            Path.Combine(Path.GetTempPath(), "Qsearch.txt"),
            TestContext.Current.CancellationToken);
        await launcher.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.False(operation.IsCompleted);
        Assert.Equal(ApartmentState.STA, launcher.ApartmentState);

        launcher.Release.Set();
        await operation;
    }

    [Fact]
    public async Task OpenAsync_UsesShellExecuteForTheExactPath()
    {
        RecordingLauncher launcher = new();
        ShellActionService service = new(launcher);
        string path = Path.Combine(Path.GetTempPath(), "Qsearch file & notes.txt");

        await service.OpenAsync(path, TestContext.Current.CancellationToken);

        ProcessStartInfo start = Assert.Single(launcher.Starts);
        Assert.Equal(path, start.FileName);
        Assert.True(start.UseShellExecute);
        Assert.Empty(start.ArgumentList);
    }

    [Fact]
    public async Task RevealAsync_QuotesOnlyTheSelectedPathForExplorer()
    {
        RecordingLauncher launcher = new();
        ShellActionService service = new(launcher);
        string path = Path.Combine(Path.GetTempPath(), "Qsearch folder", "result & file.txt");

        await service.RevealAsync(path, TestContext.Current.CancellationToken);

        ProcessStartInfo start = Assert.Single(launcher.Starts);
        Assert.Equal("explorer.exe", start.FileName);
        Assert.True(start.UseShellExecute);
        Assert.Equal($"/select,\"{path}\"", start.Arguments);
        Assert.Empty(start.ArgumentList);
    }

    [Fact]
    public async Task OpenAsync_WhenWindowsHasNoAssociation_OpensTheLocalChooser()
    {
        AssociationMissingLauncher launcher = new();
        ShellActionService service = new(launcher);
        string path = Path.Combine(Path.GetTempPath(), "unknown.qsearch-test");

        await service.OpenAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(2, launcher.Starts.Count);
        ProcessStartInfo chooser = launcher.Starts[1];
        Assert.Equal("rundll32.exe", chooser.FileName);
        Assert.False(chooser.UseShellExecute);
        Assert.Equal(["shell32.dll,OpenAs_RunDLL", path], chooser.ArgumentList);
    }

    private sealed class RecordingLauncher : IShellProcessLauncher
    {
        public List<ProcessStartInfo> Starts { get; } = [];

        public int LastThreadId { get; private set; }

        public void Start(ProcessStartInfo startInfo)
        {
            LastThreadId = Environment.CurrentManagedThreadId;
            Starts.Add(startInfo);
        }
    }

    private sealed class BlockingLauncher : IShellProcessLauncher, IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public ApartmentState ApartmentState { get; private set; } = ApartmentState.Unknown;

        public void Start(ProcessStartInfo startInfo)
        {
            ApartmentState = Thread.CurrentThread.GetApartmentState();
            Entered.TrySetResult();
            Release.Wait(TestContext.Current.CancellationToken);
        }

        public void Dispose() => Release.Dispose();
    }

    private sealed class AssociationMissingLauncher : IShellProcessLauncher
    {
        public List<ProcessStartInfo> Starts { get; } = [];

        public void Start(ProcessStartInfo startInfo)
        {
            Starts.Add(startInfo);
            if (Starts.Count == 1)
            {
                throw new Win32Exception(1155);
            }
        }
    }
}
