using System.ComponentModel;
using System.Runtime.CompilerServices;
using Qsearch.App.Services;

namespace Qsearch.App.ViewModels;

public sealed class FirstRunViewModel : INotifyPropertyChanged
{
    private readonly IMachineIndexingService _machineIndexing;
    private bool _isVisible;
    private bool _isBusy;
    private bool _isSuppressed;
    private string? _statusMessage;

    public FirstRunViewModel(
        IMachineIndexingService machineIndexing,
        bool isSuppressed = false)
    {
        ArgumentNullException.ThrowIfNull(machineIndexing);
        _machineIndexing = machineIndexing;
        _isSuppressed = isSuppressed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetField(ref _isVisible, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        MachineIndexStartResult result = await _machineIndexing.StartAsync(cancellationToken);
        Apply(result);
    }

    public async ValueTask AcceptAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            MachineIndexStartResult result = await _machineIndexing.RetryElevationAsync(
                cancellationToken);
            Apply(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ContinueFolderOnly()
    {
        SuppressPrompt();
        StatusMessage = "folder-only";
    }

    public void SuppressPrompt()
    {
        _isSuppressed = true;
        IsVisible = false;
    }

    private void Apply(MachineIndexStartResult result)
    {
        switch (result)
        {
            case MachineIndexStartResult.Started:
                IsVisible = false;
                StatusMessage = "indexing-started";
                break;
            case MachineIndexStartResult.ConsentRequired:
                IsVisible = !_isSuppressed;
                StatusMessage = "consent-required";
                break;
            case MachineIndexStartResult.ElevationDeclined:
                IsVisible = !_isSuppressed;
                StatusMessage = "elevation-declined";
                break;
            case MachineIndexStartResult.NoVolumes:
                IsVisible = false;
                StatusMessage = "no-indexable-volumes";
                break;
            default:
                IsVisible = !_isSuppressed;
                StatusMessage = "index-unavailable";
                break;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
