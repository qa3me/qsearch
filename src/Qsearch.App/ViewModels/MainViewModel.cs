using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Qsearch.App.Services;
using Qsearch.Core.Indexing;
using Qsearch.Core.Search;
using Qsearch.Core.Settings;
using Qsearch.Safety.Models;
using Qsearch.Safety.Policy;
using Qsearch.Windows.Shell;

namespace Qsearch.App.ViewModels;

public enum IndexStartupState
{
    Initializing,
    Ready,
    Unavailable,
}

public sealed class QsearchSettingsChangedEventArgs(QsearchSettings current) : EventArgs
{
    public QsearchSettings Current { get; } = current;
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IIndexStore _indexStore;
    private readonly IShellActionService _shellActions;
    private readonly IFileReviewService _reviewService;
    private readonly IFolderIndexingService? _folderIndexingService;
    private readonly ILocalSettingsStore? _settingsStore;
    private readonly IFilePreviewService? _previewService;
    private readonly IFileAssociationService? _associationService;
    private readonly Func<HotkeyChoice, HotkeyRegistrationResult>? _registerHotkey;
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private readonly SemaphoreSlim _shellActionGate = new(1, 1);
    private QsearchSettings _settings;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _reviewCancellation;
    private string _searchText = string.Empty;
    private ResultItemViewModel? _selectedResult;
    private IndexFreshness _freshness = IndexFreshness.Unavailable;
    private string? _statusMessage;
    private bool _isBusy;
    private FileReviewResult? _reviewResult;
    private FilePreviewViewModel? _filePreview;
    private bool _isPreviewLoading;
    private bool _hasPreviewError;
    private bool _isReviewLoading;
    private string? _reviewStatusMessage;
    private FileAssociationInfo? _fileAssociation;
    private FilePurposeSummary? _filePurpose;
    private Task _previewCompletion = Task.CompletedTask;
    private bool _hasConfiguredScopes;
    private IndexingSnapshot? _indexingSnapshot;
    private bool _showSettingsOnboarding;
    private bool _hasHotkeyConflict;
    private IndexStartupState _indexStartupState;
    private SearchSort _sort = new(
        SearchSortField.Modified,
        SearchSortDirection.Descending);

    public MainViewModel(
        IIndexStore indexStore,
        IShellActionService shellActions,
        IFileReviewService reviewService,
        IFolderIndexingService? folderIndexingService = null,
        FirstRunViewModel? firstRun = null,
        ILocalSettingsStore? settingsStore = null,
        QsearchSettings? initialSettings = null,
        IFilePreviewService? previewService = null,
        IFileAssociationService? associationService = null,
        Func<HotkeyChoice, HotkeyRegistrationResult>? registerHotkey = null,
        IndexStartupState startupState = IndexStartupState.Ready)
    {
        ArgumentNullException.ThrowIfNull(indexStore);
        ArgumentNullException.ThrowIfNull(shellActions);
        ArgumentNullException.ThrowIfNull(reviewService);
        _indexStore = indexStore;
        _shellActions = shellActions;
        _reviewService = reviewService;
        _folderIndexingService = folderIndexingService;
        _settingsStore = settingsStore;
        _settings = initialSettings ?? QsearchSettings.Default;
        _showSettingsOnboarding = !_settings.SettingsOnboardingCompleted;
        _previewService = previewService;
        _associationService = associationService;
        _registerHotkey = registerHotkey;
        _indexStartupState = startupState;
        _isBusy = startupState == IndexStartupState.Initializing;
        _statusMessage = startupState switch
        {
            IndexStartupState.Initializing => "index-initializing",
            IndexStartupState.Unavailable => "index-unavailable",
            _ => null,
        };
        FirstRun = firstRun;
        if (FirstRun is not null)
        {
            FirstRun.PropertyChanged += OnFirstRunPropertyChanged;
        }
        OpenCommand = new AsyncCommand(() => OpenAsync(CancellationToken.None));
        RevealCommand = new AsyncCommand(() => RevealAsync(CancellationToken.None));
        ReviewCommand = new AsyncCommand(() => ReviewAsync(CancellationToken.None));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<QsearchSettingsChangedEventArgs>? SettingsChanged;

    public ObservableCollection<ResultItemViewModel> Results { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set => SetField(ref _searchText, value ?? string.Empty);
    }

    public ResultItemViewModel? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (!SetField(ref _selectedResult, value))
            {
                return;
            }

            ResetReviewForSelectionChange();
            _previewCompletion = LoadSelectionDetailsAsync(value).AsTask();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShouldShowRightRail)));
        }
    }

    public IndexFreshness Freshness
    {
        get => _freshness;
        private set => SetField(ref _freshness, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public IndexStartupState IndexStartupState
    {
        get => _indexStartupState;
        private set => SetField(ref _indexStartupState, value);
    }

    public FileReviewResult? ReviewResult
    {
        get => _reviewResult;
        private set => SetField(ref _reviewResult, value);
    }

    public FilePreviewViewModel? FilePreview
    {
        get => _filePreview;
        private set => SetField(ref _filePreview, value);
    }

    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        private set
        {
            if (SetField(ref _isPreviewLoading, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShouldShowRightRail)));
            }
        }
    }

    public bool HasPreviewError
    {
        get => _hasPreviewError;
        private set => SetField(ref _hasPreviewError, value);
    }

    public bool IsReviewLoading
    {
        get => _isReviewLoading;
        private set => SetField(ref _isReviewLoading, value);
    }

    public string? ReviewStatusMessage
    {
        get => _reviewStatusMessage;
        private set => SetField(ref _reviewStatusMessage, value);
    }

    public FileAssociationInfo? FileAssociation
    {
        get => _fileAssociation;
        private set => SetField(ref _fileAssociation, value);
    }

    public FilePurposeSummary? FilePurpose
    {
        get => _filePurpose;
        private set => SetField(ref _filePurpose, value);
    }

    public Task PreviewCompletion => _previewCompletion;

    public bool HasConfiguredScopes
    {
        get => _hasConfiguredScopes;
        private set => SetField(ref _hasConfiguredScopes, value);
    }

    public FirstRunViewModel? FirstRun { get; }

    public IndexingSnapshot? IndexingSnapshot
    {
        get => _indexingSnapshot;
        private set => SetField(ref _indexingSnapshot, value);
    }

    public SearchSort Sort
    {
        get => _sort;
        private set => SetField(ref _sort, value);
    }

    public StartupSurface StartupSurface => _settings.StartupSurface;

    public AppThemePreference ThemePreference => _settings.ThemePreference;

    public HotkeyChoice HotkeyChoice => _settings.HotkeyChoice;

    public string Language => _settings.Language;

    public bool HasHotkeyConflict
    {
        get => _hasHotkeyConflict;
        private set => SetField(ref _hasHotkeyConflict, value);
    }

    public bool ShowSettingsOnboarding
    {
        get => _showSettingsOnboarding;
        private set
        {
            if (SetField(ref _showSettingsOnboarding, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShouldShowRightRail)));
            }
        }
    }

    public bool ShouldShowRightRail =>
        ShowSettingsOnboarding ||
        FirstRun?.IsVisible == true ||
        SelectedResult is not null ||
        IsPreviewLoading;

    public string HotkeyDisplayText => HotkeyGesture.FromChoice(HotkeyChoice).DisplayText;

    public ICommand OpenCommand { get; }

    public ICommand RevealCommand { get; }

    public ICommand ReviewCommand { get; }

    public async ValueTask SetSearchTextAsync(string text)
    {
        SearchText = text;
        if (IndexStartupState != IndexStartupState.Ready)
        {
            IsBusy = IndexStartupState == IndexStartupState.Initializing;
            StatusMessage = IndexStartupState == IndexStartupState.Initializing
                ? "index-initializing"
                : "index-unavailable";
            return;
        }

        CancellationTokenSource next = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _searchCancellation, next);
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            SearchRequest request = new(SearchQueryParser.Parse(text), 100, Sort: Sort);
            SearchPage page = await Task.Run(
                () => _indexStore.SearchAsync(request, next.Token).AsTask(),
                next.Token);
            if (!ReferenceEquals(_searchCancellation, next))
            {
                return;
            }

            ApplySearchResults(page.Results.Take(100));

            Freshness = page.Freshness;
        }
        catch (OperationCanceledException) when (next.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is DbException or IOException or InvalidOperationException)
        {
            if (ReferenceEquals(_searchCancellation, next))
            {
                Results.Clear();
                StatusMessage = "search-unavailable";
                Freshness = IndexFreshness.Unavailable;
            }
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, next))
            {
                IsBusy = false;
            }
        }
    }

    public ValueTask SetSortAsync(SearchSortField field)
    {
        SearchSortDirection direction = Sort.Field == field
            ? Sort.Direction == SearchSortDirection.Ascending
                ? SearchSortDirection.Descending
                : SearchSortDirection.Ascending
            : field == SearchSortField.Modified
                ? SearchSortDirection.Descending
                : SearchSortDirection.Ascending;
        Sort = new SearchSort(field, direction);
        return SetSearchTextAsync(SearchText);
    }

    public async ValueTask MarkIndexReadyAsync()
    {
        IndexStartupState = IndexStartupState.Ready;
        IsBusy = false;
        StatusMessage = null;
        await SetSearchTextAsync(SearchText);
    }

    public void MarkIndexUnavailable()
    {
        CancellationTokenSource? search = Interlocked.Exchange(ref _searchCancellation, null);
        search?.Cancel();
        search?.Dispose();
        IndexStartupState = IndexStartupState.Unavailable;
        IsBusy = false;
        StatusMessage = "index-unavailable";
        Freshness = IndexFreshness.Unavailable;
    }

    public async ValueTask OpenAsync(CancellationToken cancellationToken)
    {
        if (SelectedResult is not null)
        {
            await RunShellActionAsync(
                token => _shellActions.OpenAsync(SelectedResult.FullPath, token),
                cancellationToken);
        }
    }

    public async ValueTask RevealAsync(CancellationToken cancellationToken)
    {
        if (SelectedResult is not null)
        {
            await RunShellActionAsync(
                token => _shellActions.RevealAsync(SelectedResult.FullPath, token),
                cancellationToken);
        }
    }

    private async ValueTask RunShellActionAsync(
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        if (!await _shellActionGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            StatusMessage = null;
            await action(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "file-action-unavailable";
        }
        finally
        {
            _shellActionGate.Release();
        }
    }

    public async ValueTask ReviewAsync(CancellationToken cancellationToken)
    {
        ResultItemViewModel? selection = SelectedResult;
        if (selection is null)
        {
            return;
        }
        string selectedPath = selection.FullPath;
        string selectedId = selection.Id;
        long selectedSize = selection.Size;
        DateTimeOffset selectedLastWriteTimeUtc = selection.LastWriteTimeUtc;

        CancellationTokenSource next = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken reviewToken = next.Token;
        CancellationTokenSource? previous = Interlocked.Exchange(ref _reviewCancellation, next);
        previous?.Cancel();
        previous?.Dispose();
        IsReviewLoading = true;
        ReviewStatusMessage = null;
        ReviewResult = null;

        try
        {
            FileReviewResult result = await _reviewService.ReviewAsync(selectedPath, reviewToken);
            if (!ReferenceEquals(_reviewCancellation, next) ||
                !ReferenceEquals(SelectedResult, selection) ||
                !string.Equals(SelectedResult.FullPath, selectedPath, StringComparison.Ordinal) ||
                SelectedResult.Size != selectedSize ||
                SelectedResult.LastWriteTimeUtc != selectedLastWriteTimeUtc)
            {
                return;
            }

            ReviewResult = result;
            string? cacheState = GetReviewCacheState(result);
            if (cacheState is not null)
            {
                ReviewCacheRecord record = new(
                    selectedId,
                    cacheState,
                    true,
                    selectedSize,
                    selectedLastWriteTimeUtc,
                    ReviewPolicy.VersionOne.Version);
                try
                {
                    await Task.Run(
                        () => _indexStore.SaveReviewAsync(record, reviewToken).AsTask(),
                        reviewToken);
                }
                catch (OperationCanceledException) when (next.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is DbException or IOException or UnauthorizedAccessException or
                    InvalidOperationException or NotSupportedException)
                {
                    if (ReferenceEquals(_reviewCancellation, next) &&
                        ReferenceEquals(SelectedResult, selection))
                    {
                        ReviewStatusMessage = "review-cache-unavailable";
                    }
                }
            }
        }
        catch (OperationCanceledException) when (next.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            if (ReferenceEquals(_reviewCancellation, next) && ReferenceEquals(SelectedResult, selection))
            {
                ReviewStatusMessage = "review-unavailable";
            }
        }
        finally
        {
            if (ReferenceEquals(_reviewCancellation, next))
            {
                IsReviewLoading = false;
            }
        }
    }

    public async ValueTask AddFolderAsync(string path, CancellationToken cancellationToken)
    {
        if (_folderIndexingService is null)
        {
            return;
        }

        if (IndexStartupState != IndexStartupState.Ready)
        {
            IsBusy = IndexStartupState == IndexStartupState.Initializing;
            StatusMessage = IndexStartupState == IndexStartupState.Initializing
                ? "index-initializing"
                : "index-unavailable";
            return;
        }

        IsBusy = true;
        StatusMessage = "indexing";
        try
        {
            await Task.Run(
                () => _folderIndexingService.IndexFolderAsync(path, cancellationToken).AsTask(),
                cancellationToken);
            HasConfiguredScopes = true;
            Freshness = IndexFreshness.Fresh;
            StatusMessage = "index-ready";
            await SetSearchTextAsync(SearchText);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is DbException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "index-unavailable";
            Freshness = IndexFreshness.Unavailable;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async ValueTask SetStartupSurfaceAsync(
        StartupSurface surface,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(surface) || surface == _settings.StartupSurface)
        {
            return;
        }

        await SaveSettingsChangeAsync(
            settings => settings with { StartupSurface = surface, Version = 5 },
            [nameof(StartupSurface)],
            cancellationToken);
    }

    public async ValueTask SetThemePreferenceAsync(
        AppThemePreference preference,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(preference) || preference == _settings.ThemePreference)
        {
            return;
        }

        await SaveSettingsChangeAsync(
            settings => settings with { ThemePreference = preference, Version = 5 },
            [nameof(ThemePreference)],
            cancellationToken);
    }

    public async ValueTask SetHotkeyChoiceAsync(
        HotkeyChoice choice,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(choice))
        {
            return;
        }

        await SaveSettingsChangeAsync(
            settings => settings with { HotkeyChoice = choice, Version = 5 },
            [nameof(HotkeyChoice), nameof(HotkeyDisplayText)],
            cancellationToken);
    }

    public async ValueTask SetLanguageAsync(
        string language,
        CancellationToken cancellationToken)
    {
        if (language is not ("en" or "fa"))
        {
            throw new ArgumentOutOfRangeException(nameof(language));
        }

        await SaveSettingsChangeAsync(
            settings => settings with { Language = language, Version = 5 },
            [nameof(Language)],
            cancellationToken);
    }

    public async ValueTask RefreshLocalizedDetailsAsync(
        CultureInfo culture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ResultItemViewModel? selection = SelectedResult;
        if (selection is null)
        {
            return;
        }

        string selectedPath = selection.FullPath;
        FilePurposeSummary purpose = await Task.Run(
            () => FilePurposeClassifier.Classify(selectedPath, culture),
            cancellationToken);
        if (ReferenceEquals(SelectedResult, selection) &&
            string.Equals(selection.FullPath, selectedPath, StringComparison.Ordinal))
        {
            FilePurpose = purpose;
        }
    }

    public async ValueTask MarkSettingsOnboardingShownAsync(CancellationToken cancellationToken)
    {
        if (_settings.SettingsOnboardingCompleted)
        {
            return;
        }

        await SaveSettingsChangeAsync(
            settings => settings with { SettingsOnboardingCompleted = true, Version = 5 },
            [],
            cancellationToken);
    }

    public void HideSettingsOnboarding() => ShowSettingsOnboarding = false;

    public async ValueTask DismissMachineIndexPromptAsync(CancellationToken cancellationToken)
    {
        FirstRun?.SuppressPrompt();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShouldShowRightRail)));
        if (_settings.MachineIndexPromptHandled)
        {
            return;
        }

        await SaveSettingsChangeAsync(
            settings => settings with { MachineIndexPromptHandled = true, Version = 5 },
            [],
            cancellationToken);
    }

    public async ValueTask ConfirmMachineIndexConsentAsync(CancellationToken cancellationToken)
    {
        await SaveSettingsChangeAsync(
            settings => settings with
            {
                MachineIndexConsent = true,
                MachineIndexPromptHandled = true,
                Version = 5,
            },
            [],
            cancellationToken);
    }

    private async ValueTask SaveSettingsChangeAsync(
        Func<QsearchSettings, QsearchSettings> update,
        IReadOnlyList<string> changedProperties,
        CancellationToken cancellationToken)
    {
        await _settingsSaveGate.WaitAsync(cancellationToken);
        try
        {
            QsearchSettings previous = _settings;
            QsearchSettings next = update(previous);
            if (next == previous)
            {
                return;
            }

            bool hotkeyChanged = next.HotkeyChoice != previous.HotkeyChoice;
            if (hotkeyChanged && _registerHotkey is not null)
            {
                HotkeyRegistrationResult registration = _registerHotkey(next.HotkeyChoice);
                if (registration.State == HotkeyRegistrationState.Conflict)
                {
                    HasHotkeyConflict = true;
                    StatusMessage = "hotkey-conflict";
                    return;
                }

                HasHotkeyConflict = false;
            }

            _settings = next;
            NotifySettingsProperties(changedProperties);
            try
            {
                if (_settingsStore is not null)
                {
                    await _settingsStore.SaveAsync(_settings, cancellationToken);
                }

                SettingsChanged?.Invoke(this, new QsearchSettingsChangedEventArgs(_settings));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _settings = previous;
                if (hotkeyChanged)
                {
                    _ = _registerHotkey?.Invoke(previous.HotkeyChoice);
                }
                NotifySettingsProperties(changedProperties);
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _settings = previous;
                if (hotkeyChanged)
                {
                    _ = _registerHotkey?.Invoke(previous.HotkeyChoice);
                }
                NotifySettingsProperties(changedProperties);
                StatusMessage = "settings-unavailable";
            }
        }
        finally
        {
            _settingsSaveGate.Release();
        }
    }

    private void NotifySettingsProperties(IEnumerable<string> propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public void ReportStatus(string status)
    {
        StatusMessage = status;
    }

    public void ReportHotkeyConflict()
    {
        HasHotkeyConflict = true;
        StatusMessage = "hotkey-conflict";
    }

    public void ApplyIndexingSnapshot(IndexingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        IndexingSnapshot = snapshot;
        Freshness = snapshot.Freshness;
        HasConfiguredScopes |= snapshot.TotalVolumes > 0;
    }

    public async ValueTask ApplyIndexingSnapshotAsync(IndexingSnapshot snapshot)
    {
        ApplyIndexingSnapshot(snapshot);
        await SetSearchTextAsync(SearchText);
    }

    public void Dispose()
    {
        if (FirstRun is not null)
        {
            FirstRun.PropertyChanged -= OnFirstRunPropertyChanged;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _reviewCancellation?.Cancel();
        _reviewCancellation?.Dispose();
        _shellActionGate.Dispose();
        _settingsSaveGate.Dispose();
    }

    private void OnFirstRunPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(FirstRunViewModel.IsVisible))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShouldShowRightRail)));
        }
    }

    private async ValueTask LoadSelectionDetailsAsync(ResultItemViewModel? selection)
    {
        CancellationTokenSource next = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _previewCancellation, next);
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        FilePreview = null;
        FileAssociation = null;
        FilePurpose = null;
        HasPreviewError = false;
        if (selection is null)
        {
            IsPreviewLoading = false;
            return;
        }

        IsPreviewLoading = true;
        Task<FilePurposeSummary> purposeTask = Task.Run(
            () => FilePurposeClassifier.Classify(selection.FullPath, System.Globalization.CultureInfo.CurrentUICulture),
            next.Token);
        Task<FileAssociationInfo>? associationTask = _associationService is null
            ? null
            : _associationService.ResolveAsync(selection.FullPath, next.Token).AsTask();
        Task<FilePreviewData>? previewTask = _previewService is null
            ? null
            : _previewService.LoadAsync(selection.FullPath, next.Token).AsTask();
        try
        {
            if (previewTask is not null)
            {
                try
                {
                    FilePreviewData preview = await previewTask;
                    if (IsCurrentSelection(next, selection))
                    {
                        FilePreview = new FilePreviewViewModel(preview);
                        HasPreviewError = preview.PreviewMessage is not null;
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    if (IsCurrentSelection(next, selection))
                    {
                        HasPreviewError = true;
                        StatusMessage = "preview-unavailable";
                    }
                }
            }

            if (associationTask is not null)
            {
                try
                {
                    FileAssociationInfo association = await associationTask;
                    if (IsCurrentSelection(next, selection))
                    {
                        FileAssociation = association;
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
                {
                    if (IsCurrentSelection(next, selection))
                    {
                        FileAssociation = FileAssociationInfo.Unavailable;
                    }
                }
            }

            FilePurposeSummary purpose = await purposeTask;
            if (IsCurrentSelection(next, selection))
            {
                FilePurpose = purpose;
            }
        }
        catch (OperationCanceledException) when (next.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, next))
            {
                IsPreviewLoading = false;
            }
        }
    }

    private bool IsCurrentSelection(CancellationTokenSource cancellation, ResultItemViewModel selection) =>
        ReferenceEquals(_previewCancellation, cancellation) &&
        ReferenceEquals(SelectedResult, selection);

    private void ApplySearchResults(IEnumerable<SearchResult> searchResults)
    {
        ResultItemViewModel? selected = SelectedResult;
        Dictionary<string, ResultItemViewModel> existing = Results
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        List<ResultItemViewModel> desired = [];
        bool selectedDetailsChanged = false;
        foreach (SearchResult result in searchResults)
        {
            if (!existing.TryGetValue(result.Id, out ResultItemViewModel? item))
            {
                item = new ResultItemViewModel(result);
            }
            else if (item.UpdateFrom(result) && ReferenceEquals(item, selected))
            {
                selectedDetailsChanged = true;
            }

            desired.Add(item);
        }

        for (int position = 0; position < desired.Count; position++)
        {
            ResultItemViewModel item = desired[position];
            if (position < Results.Count && ReferenceEquals(Results[position], item))
            {
                continue;
            }

            int existingPosition = Results.IndexOf(item);
            if (existingPosition >= 0)
            {
                Results.Move(existingPosition, position);
            }
            else
            {
                Results.Insert(position, item);
            }
        }

        while (Results.Count > desired.Count)
        {
            Results.RemoveAt(Results.Count - 1);
        }

        if (selected is not null && !desired.Contains(selected))
        {
            SelectedResult = null;
        }
        else if (selectedDetailsChanged)
        {
            ResetReviewForSelectionChange();
            _previewCompletion = LoadSelectionDetailsAsync(selected).AsTask();
        }
    }

    private void ResetReviewForSelectionChange()
    {
        CancellationTokenSource? review = Interlocked.Exchange(ref _reviewCancellation, null);
        review?.Cancel();
        review?.Dispose();
        ReviewResult = null;
        ReviewStatusMessage = null;
        IsReviewLoading = false;
    }

    private static string? GetReviewCacheState(FileReviewResult result)
    {
        if (!result.IsRiskSearchable)
        {
            return null;
        }

        return result.State switch
        {
            ReviewState.HighCaution => "high",
            ReviewState.ReviewSuggested => "review",
            ReviewState.NoNotableSignals => "none",
            _ => null,
        };
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

    private sealed class AsyncCommand(Func<ValueTask> action) : ICommand
    {
        private int _isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => Volatile.Read(ref _isExecuting) == 0;

        public async void Execute(object? parameter)
        {
            if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
            {
                return;
            }

            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try
            {
                await action();
            }
            finally
            {
                Interlocked.Exchange(ref _isExecuting, 0);
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
