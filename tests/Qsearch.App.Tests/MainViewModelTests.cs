using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Data.Common;
using Qsearch.App.Services;
using Qsearch.App.ViewModels;
using Qsearch.Core.Indexing;
using Qsearch.Core.Settings;
using Qsearch.Index.Sqlite;
using Qsearch.Safety.Models;
using Qsearch.Windows.Shell;

namespace Qsearch.App.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task SynchronousDatabaseSearch_DoesNotBlockTheCallingThread()
    {
        SynchronousBlockingIndexStore index = new();
        MainViewModel viewModel = new(index, new StubShellActions(), new StubReviewService());
        TaskCompletionSource<Task> invocationReturned = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task caller = Task.Run(
            () =>
            {
                ValueTask search = viewModel.SetSearchTextAsync("blocking");
                invocationReturned.TrySetResult(search.AsTask());
            },
            TestContext.Current.CancellationToken);

        await index.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        bool returnedWithoutWaitingForDatabase = await Task.WhenAny(
            invocationReturned.Task,
            Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken)) ==
            invocationReturned.Task;

        index.Release.Set();
        await caller.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        if (invocationReturned.Task.IsCompletedSuccessfully)
        {
            Task search = await invocationReturned.Task;
            await search.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
        }

        Assert.True(
            returnedWithoutWaitingForDatabase,
            "SetSearchTextAsync synchronously entered the SQLite-backed store and would freeze WPF input.");
    }

    [Fact]
    public async Task SynchronousFolderIndex_DoesNotBlockTheCallingThread()
    {
        SynchronousBlockingFolderIndexer indexer = new();
        MainViewModel viewModel = new(
            new ImmediateIndexStore([]),
            new StubShellActions(),
            new StubReviewService(),
            folderIndexingService: indexer);
        TaskCompletionSource<Task> invocationReturned = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task caller = Task.Run(
            () =>
            {
                ValueTask indexing = viewModel.AddFolderAsync(
                    @"C:\scope",
                    TestContext.Current.CancellationToken);
                invocationReturned.TrySetResult(indexing.AsTask());
            },
            TestContext.Current.CancellationToken);

        await indexer.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        bool returnedWithoutWaitingForScan = await Task.WhenAny(
            invocationReturned.Task,
            Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken)) ==
            invocationReturned.Task;

        indexer.Release.Set();
        await caller.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        if (invocationReturned.Task.IsCompletedSuccessfully)
        {
            Task indexing = await invocationReturned.Task;
            await indexing.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
        }

        Assert.True(
            returnedWithoutWaitingForScan,
            "AddFolderAsync synchronously entered the scanner and would freeze WPF input.");
    }

    [Fact]
    public async Task TypingAgain_CancelsObsoleteSearchAndShowsLatestRows()
    {
        ControlledIndexStore index = new();
        MainViewModel viewModel = new(index, new StubShellActions(), new StubReviewService());

        ValueTask first = viewModel.SetSearchTextAsync("old");
        await index.Started("old").WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        ValueTask second = viewModel.SetSearchTextAsync("new");
        await index.Started("new").WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        index.Complete("new", Results("new.txt"));
        await second;
        await first;

        Assert.Equal(["new.txt"], viewModel.Results.Select(result => result.Name));
        Assert.True(index.WasCancelled("old"));
    }

    [Fact]
    public async Task Search_CapsVisibleRowsAtOneHundredAndRecoversFromError()
    {
        ControlledIndexStore index = new();
        MainViewModel viewModel = new(index, new StubShellActions(), new StubReviewService());
        ValueTask search = viewModel.SetSearchTextAsync("many");
        await index.Started("many").WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        index.Complete(
            "many",
            Enumerable.Range(0, 120).Select(position => Result($"item-{position}.txt")).ToArray());

        await search;

        Assert.Equal(100, viewModel.Results.Count);

        ValueTask failed = viewModel.SetSearchTextAsync("failure");
        await index.Started("failure").WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        index.Fail("failure", new IOException("gone"));
        await failed;
        Assert.NotNull(viewModel.StatusMessage);

        ValueTask databaseFailure = viewModel.SetSearchTextAsync("database-failure");
        await index.Started("database-failure").WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        index.Fail("database-failure", new StubDbException());
        await databaseFailure;
        Assert.Equal("search-unavailable", viewModel.StatusMessage);
    }

    [Fact]
    public async Task EmptyQuery_LoadsTheDefaultRootViewAndSortCanChange()
    {
        ControlledIndexStore index = new();
        MainViewModel viewModel = new(index, new StubShellActions(), new StubReviewService());

        ValueTask initial = viewModel.SetSearchTextAsync(string.Empty);
        await index.Started(string.Empty).WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        index.Complete(string.Empty, Results("newest.txt"));
        await initial;
        ValueTask sorted = viewModel.SetSortAsync(SearchSortField.Name);
        await index.Started(string.Empty).WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        index.Complete(string.Empty, Results("alpha.txt"));
        await sorted;

        Assert.Equal(["alpha.txt"], viewModel.Results.Select(result => result.Name));
        Assert.Equal(
            new SearchSort(SearchSortField.Name, SearchSortDirection.Ascending),
            index.LastRequest!.Sort);
    }

    [Fact]
    public void HostileFilename_ExposesRawPathButNeutralizesDisplayControls()
    {
        SearchResult result = Result("invoice\u202Efdp.exe");

        ResultItemViewModel item = new(result);

        Assert.Equal("invoice\u202Efdp.exe", item.Name);
        Assert.DoesNotContain('\u202E', item.DisplayName);
        Assert.Contains("RLO", item.DisplayName, StringComparison.Ordinal);
        Assert.Equal(item.DisplayName, item.ToString());
        Assert.Equal(result.FullPath, item.FullPath);
    }

    [Fact]
    public async Task ShellActions_ReceiveExactPathWithoutCommandConcatenation()
    {
        ControlledIndexStore index = new();
        StubShellActions shell = new();
        MainViewModel viewModel = new(index, shell, new StubReviewService())
        {
            SelectedResult = new ResultItemViewModel(Result("a & b.txt")),
        };

        await viewModel.RevealAsync(TestContext.Current.CancellationToken);

        Assert.Equal(@"C:\scope\a & b.txt", shell.RevealedPath);
    }

    [Fact]
    public async Task ShellActionFailure_IsReportedWithoutCrashingTheUiCommand()
    {
        ImmediateIndexStore index = new(Results("selected.txt"));
        ThrowingShellActions shell = new();
        MainViewModel viewModel = new(index, shell, new StubReviewService());
        await viewModel.SetSearchTextAsync(string.Empty);
        viewModel.SelectedResult = Assert.Single(viewModel.Results);

        await viewModel.OpenAsync(TestContext.Current.CancellationToken);

        Assert.Equal("file-action-unavailable", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RepeatedOpenClick_DoesNotLaunchTheSameSelectionTwiceConcurrently()
    {
        BlockingShellActions shell = new();
        MainViewModel viewModel = new(
            new ImmediateIndexStore([]),
            shell,
            new StubReviewService())
        {
            SelectedResult = new ResultItemViewModel(Result("selected.txt")),
        };

        Task first = viewModel.OpenAsync(TestContext.Current.CancellationToken).AsTask();
        await shell.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        Task second = viewModel.OpenAsync(TestContext.Current.CancellationToken).AsTask();

        Assert.Equal(1, shell.OpenCalls);
        shell.Release.TrySetResult();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task IndexSnapshot_RefreshesVisibleRootRowsWhenSearchIsEmpty()
    {
        ImmediateIndexStore index = new(Results("cached.txt"));
        MainViewModel viewModel = new(index, new StubShellActions(), new StubReviewService());
        await viewModel.SetSearchTextAsync(string.Empty);
        index.Results = Results("newly-indexed.txt");

        await viewModel.ApplyIndexingSnapshotAsync(
            new IndexingSnapshot(1, 1, 0, IndexFreshness.CatchingUp, 0.5, []));

        Assert.Equal(2, index.SearchCount);
        Assert.Equal(["newly-indexed.txt"], viewModel.Results.Select(result => result.Name));
    }

    [Fact]
    public async Task BackgroundRefresh_KeepsASelectedResultThatIsStillVisible()
    {
        ImmediateIndexStore index = new(Results("selected.txt", "old.txt"));
        MainViewModel viewModel = new(index, new StubShellActions(), new StubReviewService());
        await viewModel.SetSearchTextAsync(string.Empty);
        ResultItemViewModel selected = viewModel.Results[0];
        viewModel.SelectedResult = selected;
        List<NotifyCollectionChangedAction> changes = [];
        viewModel.Results.CollectionChanged += (_, eventArgs) => changes.Add(eventArgs.Action);
        index.Results = Results("new.txt", "selected.txt");

        await viewModel.ApplyIndexingSnapshotAsync(
            new IndexingSnapshot(1, 1, 0, IndexFreshness.CatchingUp, 0.5, []));

        Assert.Same(selected, viewModel.SelectedResult);
        Assert.Contains(selected, viewModel.Results);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, changes);
    }

    [Fact]
    public async Task BackgroundRefresh_UpdatesThePathOfASelectedStableIdBeforeOpen()
    {
        SearchResult oldResult = new(
            "stable-id", "scope-a", "old.txt", @"C:\scope\old.txt",
            IndexItemType.File, 1, DateTimeOffset.UnixEpoch);
        SearchResult renamed = oldResult with
        {
            Name = "renamed.txt",
            FullPath = @"C:\scope\renamed.txt",
        };
        ImmediateIndexStore index = new([oldResult]);
        StubShellActions shell = new();
        MainViewModel viewModel = new(index, shell, new StubReviewService());
        await viewModel.SetSearchTextAsync(string.Empty);
        ResultItemViewModel selected = Assert.Single(viewModel.Results);
        viewModel.SelectedResult = selected;
        index.Results = [renamed];

        await viewModel.SetSearchTextAsync(string.Empty);
        await viewModel.OpenAsync(TestContext.Current.CancellationToken);

        Assert.Same(selected, viewModel.SelectedResult);
        Assert.Equal("renamed.txt", selected.Name);
        Assert.Equal(@"C:\scope\renamed.txt", selected.FullPath);
        Assert.Equal(@"C:\scope\renamed.txt", shell.OpenedPath);
    }

    [Fact]
    public async Task Review_ReportsProgressCachesEvidenceAndSuppressesStaleCompletion()
    {
        ImmediateIndexStore index = new([]);
        ControlledReviewService reviews = new();
        MainViewModel viewModel = new(index, new StubShellActions(), reviews);
        ResultItemViewModel first = new(Result("first.exe"));
        ResultItemViewModel second = new(Result("second.txt"));
        viewModel.SelectedResult = first;

        Task firstReview = viewModel.ReviewAsync(TestContext.Current.CancellationToken).AsTask();
        await reviews.Started("first.exe");
        Assert.True(viewModel.IsReviewLoading);
        viewModel.SelectedResult = second;
        reviews.Complete("first.exe", ReviewState.ReviewSuggested);
        await firstReview;

        Assert.Null(viewModel.ReviewResult);
        Task secondReview = viewModel.ReviewAsync(TestContext.Current.CancellationToken).AsTask();
        await reviews.Started("second.txt");
        reviews.Complete("second.txt", ReviewState.NoNotableSignals);
        await secondReview;

        Assert.False(viewModel.IsReviewLoading);
        Assert.Equal(ReviewState.NoNotableSignals, viewModel.ReviewResult!.State);
        ReviewCacheRecord cached = Assert.Single(index.SavedReviews);
        Assert.Equal(second.Id, cached.ItemId);
        Assert.Equal("none", cached.State);
    }

    [Fact]
    public async Task ReviewFailure_IsVisibleAndDoesNotRemoveFileActions()
    {
        MainViewModel viewModel = new(
            new ImmediateIndexStore([]),
            new StubShellActions(),
            new ThrowingReviewService(),
            previewService: new ThrowingPreviewService())
        {
            SelectedResult = new ResultItemViewModel(Result("selected.txt")),
        };
        await viewModel.PreviewCompletion;

        await viewModel.ReviewAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.HasPreviewError);
        Assert.Equal("review-unavailable", viewModel.ReviewStatusMessage);
        Assert.NotNull(viewModel.SelectedResult);
    }

    [Fact]
    public async Task PreviewContentFailure_KeepsMetadataAndFileActions()
    {
        StubShellActions shell = new();
        MainViewModel viewModel = new(
            new ImmediateIndexStore([]),
            shell,
            new StubReviewService(),
            previewService: new MetadataOnlyPreviewService())
        {
            SelectedResult = new ResultItemViewModel(Result("selected.txt")),
        };

        await viewModel.PreviewCompletion;
        await viewModel.OpenAsync(TestContext.Current.CancellationToken);
        await viewModel.RevealAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(viewModel.FilePreview);
        Assert.Equal("42 B", viewModel.FilePreview.SizeText);
        Assert.True(viewModel.HasPreviewError);
        Assert.Equal(viewModel.SelectedResult!.FullPath, shell.OpenedPath);
        Assert.Equal(viewModel.SelectedResult.FullPath, shell.RevealedPath);
    }

    [Theory]
    [InlineData(ReviewState.ReviewSuggested, "review", true, false)]
    [InlineData(ReviewState.HighCaution, "high", true, true)]
    [InlineData(ReviewState.NoNotableSignals, "none", false, false)]
    public async Task UiReview_IsFindableThroughRiskFilters(
        ReviewState state,
        string persistedState,
        bool inReview,
        bool inHigh)
    {
        string root = Path.Combine(Path.GetTempPath(), "Qsearch.Review.Ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using SqliteIndexStore store = new(Path.Combine(root, "index.db"), TimeProvider.System);
            CancellationToken token = TestContext.Current.CancellationToken;
            await store.InitializeAsync(token);
            string path = Path.Combine(root, "reviewed.exe");
            IndexItem item = new(
                "review-id",
                "scope",
                "reviewed.exe",
                "reviewed.exe",
                path,
                null,
                IndexItemType.File,
                7,
                DateTimeOffset.UnixEpoch,
                FileAttributes.Normal);
            await store.ApplyBatchAsync(
                new IndexBatch(
                    "scope",
                    [new IndexChange(IndexChangeKind.Upsert, "scope", item)]),
                token);
            using MainViewModel viewModel = new(
                store,
                new StubShellActions(),
                new StubReviewService(state));
            await viewModel.SetSearchTextAsync(string.Empty);
            viewModel.SelectedResult = Assert.Single(viewModel.Results);

            await viewModel.ReviewAsync(token);

            Assert.Equal(persistedState, (await store.GetReviewAsync(item.Id, token))!.State);
            await viewModel.SetSearchTextAsync("risk:review");
            Assert.Equal(inReview ? 1 : 0, viewModel.Results.Count);
            await viewModel.SetSearchTextAsync("risk:high");
            Assert.Equal(inHigh ? 1 : 0, viewModel.Results.Count);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ReviewCacheWrite_RunsAwayFromTheCallingThread()
    {
        int callerThread = 0;
        int writerThread = 0;
        Exception? failure = null;
        ImmediateIndexStore index = new([])
        {
            ReviewWriter = (_, _) =>
            {
                writerThread = Environment.CurrentManagedThreadId;
                return ValueTask.CompletedTask;
            },
        };
        MainViewModel viewModel = new(index, new StubShellActions(), new StubReviewService())
        {
            SelectedResult = new ResultItemViewModel(Result("reviewed.exe")),
        };
        Thread caller = new(() =>
        {
            try
            {
                callerThread = Environment.CurrentManagedThreadId;
                viewModel.ReviewAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        caller.Start();
        Assert.True(caller.Join(TimeSpan.FromSeconds(5)));

        Assert.Null(failure);
        Assert.NotNull(viewModel.ReviewResult);
        Assert.NotEqual(0, writerThread);
        Assert.NotEqual(callerThread, writerThread);
    }

    [Fact]
    public async Task DatabaseReviewCacheFailure_LeavesCompletedReviewVisible()
    {
        ImmediateIndexStore index = new([])
        {
            ReviewWriter = (_, _) => ValueTask.FromException(new StubDbException()),
        };
        MainViewModel viewModel = new(
            index,
            new StubShellActions(),
            new StubReviewService(ReviewState.HighCaution))
        {
            SelectedResult = new ResultItemViewModel(Result("reviewed.exe")),
        };

        await viewModel.ReviewAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReviewState.HighCaution, viewModel.ReviewResult!.State);
        Assert.Equal("review-cache-unavailable", viewModel.ReviewStatusMessage);
        Assert.False(viewModel.IsReviewLoading);
        Assert.NotNull(viewModel.SelectedResult);
    }

    [Fact]
    public async Task MetadataRefresh_CancelsReviewForThePreviousFileVersion()
    {
        SearchResult original = Result("reviewed.exe");
        ImmediateIndexStore index = new([original]);
        ControlledReviewService reviews = new();
        MainViewModel viewModel = new(index, new StubShellActions(), reviews);
        await viewModel.SetSearchTextAsync(string.Empty);
        viewModel.SelectedResult = Assert.Single(viewModel.Results);
        Task review = viewModel.ReviewAsync(TestContext.Current.CancellationToken).AsTask();
        await reviews.Started("reviewed.exe");
        index.Results = [original with { Size = original.Size + 1 }];

        await viewModel.SetSearchTextAsync(string.Empty);
        reviews.Complete("reviewed.exe", ReviewState.HighCaution);
        await review;

        Assert.Null(viewModel.ReviewResult);
        Assert.Empty(index.SavedReviews);
    }

    [Fact]
    public async Task AddFolder_RefreshesTheEmptyRootViewAfterIndexingCompletes()
    {
        ImmediateIndexStore index = new(Results("cached.txt"));
        CompletingFolderIndexer folders = new(() => index.Results = Results("added.txt"));
        MainViewModel viewModel = new(
            index,
            new StubShellActions(),
            new StubReviewService(),
            folders);
        await viewModel.SetSearchTextAsync(string.Empty);

        await viewModel.AddFolderAsync(@"C:\scope", TestContext.Current.CancellationToken);

        Assert.Equal(2, index.SearchCount);
        Assert.Equal(["added.txt"], viewModel.Results.Select(result => result.Name));
    }

    [Fact]
    public async Task AddFolder_DoesNotTouchTheStoreDuringColdInitialization()
    {
        CountingFolderIndexer folders = new();
        MainViewModel viewModel = new(
            new ImmediateIndexStore([]),
            new StubShellActions(),
            new StubReviewService(),
            folders,
            startupState: IndexStartupState.Initializing);

        await viewModel.AddFolderAsync(@"C:\scope", TestContext.Current.CancellationToken);

        Assert.Equal(0, folders.Calls);
        Assert.Equal("index-initializing", viewModel.StatusMessage);
    }

    [Fact]
    public async Task StartupSurfaceChoice_IsPersistedForTheNextLaunch()
    {
        MemorySettingsStore settings = new(QsearchSettings.Default);
        MainViewModel viewModel = new(
            new ImmediateIndexStore([]),
            new StubShellActions(),
            new StubReviewService(),
            settingsStore: settings,
            initialSettings: settings.Current);

        await viewModel.SetStartupSurfaceAsync(
            StartupSurface.Compact,
            TestContext.Current.CancellationToken);

        Assert.Equal(StartupSurface.Compact, viewModel.StartupSurface);
        Assert.Equal(StartupSurface.Compact, settings.Current.StartupSurface);
    }

    [Fact]
    public async Task AppearanceAndShortcutChoices_ArePersistedAndPublished()
    {
        MemorySettingsStore settings = new(QsearchSettings.Default);
        MainViewModel viewModel = new(
            new ImmediateIndexStore([]),
            new StubShellActions(),
            new StubReviewService(),
            settingsStore: settings,
            initialSettings: settings.Current);
        List<QsearchSettings> published = [];
        viewModel.SettingsChanged += (_, eventArgs) => published.Add(eventArgs.Current);

        await viewModel.SetThemePreferenceAsync(
            AppThemePreference.Light,
            TestContext.Current.CancellationToken);
        await viewModel.SetHotkeyChoiceAsync(
            HotkeyChoice.ControlShiftSpace,
            TestContext.Current.CancellationToken);

        Assert.Equal(AppThemePreference.Light, viewModel.ThemePreference);
        Assert.Equal(HotkeyChoice.ControlShiftSpace, viewModel.HotkeyChoice);
        Assert.Equal(AppThemePreference.Light, settings.Current.ThemePreference);
        Assert.Equal(HotkeyChoice.ControlShiftSpace, settings.Current.HotkeyChoice);
        Assert.Equal(2, published.Count);
    }

    [Fact]
    public async Task ConflictingShortcut_IsNotSavedOrPublished()
    {
        QsearchSettings initial = QsearchSettings.Default with { HotkeyChoice = HotkeyChoice.AltQ };
        MemorySettingsStore settings = new(initial);
        MainViewModel viewModel = new(
            new ImmediateIndexStore([]),
            new StubShellActions(),
            new StubReviewService(),
            settingsStore: settings,
            initialSettings: initial,
            registerHotkey: _ => new HotkeyRegistrationResult(HotkeyRegistrationState.Conflict));
        int published = 0;
        viewModel.SettingsChanged += (_, _) => published++;

        await viewModel.SetHotkeyChoiceAsync(
            HotkeyChoice.WindowsS,
            TestContext.Current.CancellationToken);

        Assert.Equal(HotkeyChoice.AltQ, viewModel.HotkeyChoice);
        Assert.Equal(HotkeyChoice.AltQ, settings.Current.HotkeyChoice);
        Assert.Equal(0, settings.SaveCount);
        Assert.Equal(0, published);
        Assert.True(viewModel.HasHotkeyConflict);
    }

    [Fact]
    public async Task SearchWhileInitializing_DefersOnlyTheLatestQueryUntilReady()
    {
        ImmediateIndexStore index = new(Results("latest.txt"));
        MainViewModel viewModel = new(
            index,
            new StubShellActions(),
            new StubReviewService(),
            startupState: IndexStartupState.Initializing);

        await viewModel.SetSearchTextAsync("old");
        await viewModel.SetSearchTextAsync("latest");
        Assert.Equal(0, index.SearchCount);

        await viewModel.MarkIndexReadyAsync();

        Assert.Equal(IndexStartupState.Ready, viewModel.IndexStartupState);
        Assert.Equal(1, index.SearchCount);
        Assert.Equal("latest", viewModel.SearchText);
    }

    [Fact]
    public async Task UnavailableIndex_NeverStartsSearch()
    {
        ImmediateIndexStore index = new(Results("ignored.txt"));
        MainViewModel viewModel = new(
            index,
            new StubShellActions(),
            new StubReviewService(),
            startupState: IndexStartupState.Initializing);
        viewModel.MarkIndexUnavailable();

        await viewModel.SetSearchTextAsync("ignored");

        Assert.Equal(0, index.SearchCount);
        Assert.Equal(IndexStartupState.Unavailable, viewModel.IndexStartupState);
        Assert.Equal("index-unavailable", viewModel.StatusMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task LanguageChoice_IsPersistedAndRefreshesPurposeText()
    {
        MemorySettingsStore settings = new(QsearchSettings.Default);
        MainViewModel viewModel = new(
            new ImmediateIndexStore([]),
            new StubShellActions(),
            new StubReviewService(),
            settingsStore: settings,
            initialSettings: settings.Current)
        {
            SelectedResult = new ResultItemViewModel(Result("notes.txt")),
        };
        await viewModel.PreviewCompletion;

        await viewModel.SetLanguageAsync("fa", TestContext.Current.CancellationToken);
        await viewModel.RefreshLocalizedDetailsAsync(
            System.Globalization.CultureInfo.GetCultureInfo("fa-IR"),
            TestContext.Current.CancellationToken);

        Assert.Equal("fa", viewModel.Language);
        Assert.Equal("fa", settings.Current.Language);
        Assert.NotNull(viewModel.FilePurpose);
        Assert.Contains("فایل", viewModel.FilePurpose.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FirstSessionSettingsCard_PersistsOnceAndCanCollapseWithoutLosingPreviewRail()
    {
        MemorySettingsStore settings = new(QsearchSettings.Default);
        MainViewModel viewModel = new(
            new ImmediateIndexStore([]),
            new StubShellActions(),
            new StubReviewService(),
            settingsStore: settings,
            initialSettings: settings.Current);
        System.Reflection.PropertyInfo? showOnboarding = viewModel.GetType().GetProperty("ShowSettingsOnboarding");
        System.Reflection.PropertyInfo? shouldShowRail = viewModel.GetType().GetProperty("ShouldShowRightRail");
        System.Reflection.MethodInfo? markShown = viewModel.GetType().GetMethod("MarkSettingsOnboardingShownAsync");
        System.Reflection.MethodInfo? hide = viewModel.GetType().GetMethod("HideSettingsOnboarding");
        Assert.NotNull(showOnboarding);
        Assert.NotNull(shouldShowRail);
        Assert.NotNull(markShown);
        Assert.NotNull(hide);
        Assert.True(Assert.IsType<bool>(showOnboarding.GetValue(viewModel)));
        Assert.True(Assert.IsType<bool>(shouldShowRail.GetValue(viewModel)));

        ValueTask save = Assert.IsType<ValueTask>(markShown.Invoke(
            viewModel,
            [TestContext.Current.CancellationToken]));
        await save;
        hide.Invoke(viewModel, null);

        Assert.False(Assert.IsType<bool>(showOnboarding.GetValue(viewModel)));
        Assert.False(Assert.IsType<bool>(shouldShowRail.GetValue(viewModel)));
        Assert.True(ReadBoolean(settings.Current, "SettingsOnboardingCompleted"));

        viewModel.SelectedResult = new ResultItemViewModel(Result("preview.txt"));
        Assert.True(Assert.IsType<bool>(shouldShowRail.GetValue(viewModel)));
    }

    [Fact]
    public async Task HandlingMachinePrompt_PersistsDismissalBeforeRetryResult()
    {
        StubMachineIndexingService machine = new(MachineIndexStartResult.ConsentRequired);
        FirstRunViewModel firstRun = new(machine);
        await firstRun.InitializeAsync(TestContext.Current.CancellationToken);
        MemorySettingsStore settings = new(QsearchSettings.Default);
        MainViewModel viewModel = new(
            new ImmediateIndexStore([]),
            new StubShellActions(),
            new StubReviewService(),
            firstRun: firstRun,
            settingsStore: settings,
            initialSettings: settings.Current);
        System.Reflection.MethodInfo? dismiss = viewModel.GetType().GetMethod("DismissMachineIndexPromptAsync");
        Assert.NotNull(dismiss);

        ValueTask save = Assert.IsType<ValueTask>(dismiss.Invoke(
            viewModel,
            [TestContext.Current.CancellationToken]));
        await save;

        Assert.False(firstRun.IsVisible);
        Assert.True(ReadBoolean(settings.Current, "MachineIndexPromptHandled"));
    }

    [Fact]
    public async Task SuccessfulMachineIndexConsent_IsKeptWhenLaterSettingsChange()
    {
        MemorySettingsStore settings = new(QsearchSettings.Default);
        MainViewModel viewModel = new(
            new ImmediateIndexStore([]),
            new StubShellActions(),
            new StubReviewService(),
            settingsStore: settings,
            initialSettings: settings.Current);

        await viewModel.ConfirmMachineIndexConsentAsync(TestContext.Current.CancellationToken);
        await viewModel.SetThemePreferenceAsync(
            AppThemePreference.Light,
            TestContext.Current.CancellationToken);

        Assert.True(settings.Current.MachineIndexConsent);
        Assert.True(settings.Current.MachineIndexPromptHandled);
    }

    [Fact]
    public async Task RapidSelection_DoesNotLetAnOlderPreviewReplaceTheCurrentFile()
    {
        ControlledPreviewService previews = new();
        MainViewModel viewModel = new(
            new ImmediateIndexStore([]),
            new StubShellActions(),
            new StubReviewService(),
            previewService: previews);
        ResultItemViewModel first = new(Result("first.txt"));
        ResultItemViewModel second = new(Result("second.txt"));

        viewModel.SelectedResult = first;
        await previews.Started("first.txt");
        Task firstLoad = viewModel.PreviewCompletion;
        viewModel.SelectedResult = second;
        await previews.Started("second.txt");
        previews.Complete("second.txt");
        await viewModel.PreviewCompletion;
        previews.Complete("first.txt");
        await firstLoad;

        Assert.Equal("second.txt", viewModel.FilePreview!.Name);
        Assert.Equal(second.FullPath, viewModel.FilePreview.FullPath);
    }

    private static SearchResult[] Results(params string[] names)
    {
        return names.Select(Result).ToArray();
    }

    private static SearchResult Result(string name)
    {
        return new SearchResult(
            name,
            "scope-a",
            name,
            Path.Combine(@"C:\scope", name),
            IndexItemType.File,
            1,
            DateTimeOffset.UnixEpoch);
    }

    private sealed class ControlledIndexStore : IIndexStore
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<SearchPage>> _pending = [];
        private readonly ConcurrentDictionary<string, bool> _cancelled = [];
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _started = [];

        public ValueTask<SearchPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            string query = request.Query.IncludeTerms.Count == 0
                ? string.Empty
                : request.Query.IncludeTerms[0].Value;
            TaskCompletionSource<SearchPage> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[query] = completion;
            _started.GetOrAdd(query, static _ => new SemaphoreSlim(0)).Release();
            cancellationToken.Register(() =>
            {
                _cancelled[query] = true;
                completion.TrySetCanceled(cancellationToken);
            });
            return new ValueTask<SearchPage>(completion.Task);
        }

        public SearchRequest? LastRequest { get; private set; }

        public Task Started(string query) =>
            _started.GetOrAdd(query, static _ => new SemaphoreSlim(0)).WaitAsync();

        public void Complete(string query, IReadOnlyList<SearchResult> results)
        {
            _pending[query].TrySetResult(new SearchPage(results, IndexFreshness.Fresh));
        }

        public void Fail(string query, Exception exception)
        {
            _pending[query].TrySetException(exception);
        }

        public bool WasCancelled(string query) => _cancelled.ContainsKey(query);

        public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ApplyBatchAsync(IndexBatch batch, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<long> CountAsync(CancellationToken cancellationToken) => ValueTask.FromResult(0L);
        public ValueTask ResetScopeAsync(string scopeId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<IndexCheckpoint?> GetCheckpointAsync(string scopeId, CancellationToken cancellationToken) => ValueTask.FromResult<IndexCheckpoint?>(null);
        public ValueTask SaveCheckpointAsync(IndexCheckpoint checkpoint, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<ReviewCacheRecord?> GetReviewAsync(string itemId, CancellationToken cancellationToken) => ValueTask.FromResult<ReviewCacheRecord?>(null);
        public ValueTask SaveReviewAsync(ReviewCacheRecord review, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class SynchronousBlockingIndexStore : IIndexStore
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim Release { get; } = new(initialState: false);

        public ValueTask<SearchPage> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            Release.Wait(cancellationToken);
            return ValueTask.FromResult(new SearchPage([], IndexFreshness.Fresh));
        }

        public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ApplyBatchAsync(IndexBatch batch, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<long> CountAsync(CancellationToken cancellationToken) => ValueTask.FromResult(0L);
        public ValueTask ResetScopeAsync(string scopeId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<IndexCheckpoint?> GetCheckpointAsync(string scopeId, CancellationToken cancellationToken) => ValueTask.FromResult<IndexCheckpoint?>(null);
        public ValueTask SaveCheckpointAsync(IndexCheckpoint checkpoint, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<ReviewCacheRecord?> GetReviewAsync(string itemId, CancellationToken cancellationToken) => ValueTask.FromResult<ReviewCacheRecord?>(null);
        public ValueTask SaveReviewAsync(ReviewCacheRecord review, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class ImmediateIndexStore(IReadOnlyList<SearchResult> results) : IIndexStore
    {
        public IReadOnlyList<SearchResult> Results { get; set; } = results;

        public int SearchCount { get; private set; }

        public List<ReviewCacheRecord> SavedReviews { get; } = [];

        public Func<ReviewCacheRecord, CancellationToken, ValueTask>? ReviewWriter { get; init; }

        public ValueTask<SearchPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            SearchCount++;
            return ValueTask.FromResult(new SearchPage(Results, IndexFreshness.Fresh));
        }

        public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ApplyBatchAsync(IndexBatch batch, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<long> CountAsync(CancellationToken cancellationToken) => ValueTask.FromResult((long)Results.Count);
        public ValueTask ResetScopeAsync(string scopeId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<IndexCheckpoint?> GetCheckpointAsync(string scopeId, CancellationToken cancellationToken) => ValueTask.FromResult<IndexCheckpoint?>(null);
        public ValueTask SaveCheckpointAsync(IndexCheckpoint checkpoint, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<ReviewCacheRecord?> GetReviewAsync(string itemId, CancellationToken cancellationToken) => ValueTask.FromResult<ReviewCacheRecord?>(null);
        public ValueTask SaveReviewAsync(ReviewCacheRecord review, CancellationToken cancellationToken)
        {
            SavedReviews.Add(review);
            return ReviewWriter?.Invoke(review, cancellationToken) ?? ValueTask.CompletedTask;
        }
    }

    private sealed class CompletingFolderIndexer(Action completed) : IFolderIndexingService
    {
        public ValueTask IndexFolderAsync(string path, CancellationToken cancellationToken)
        {
            completed();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingFolderIndexer : IFolderIndexingService
    {
        public int Calls { get; private set; }

        public ValueTask IndexFolderAsync(string path, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SynchronousBlockingFolderIndexer : IFolderIndexingService
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim Release { get; } = new(initialState: false);

        public ValueTask IndexFolderAsync(string path, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            Release.Wait(cancellationToken);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MemorySettingsStore(QsearchSettings current) : ILocalSettingsStore
    {
        public QsearchSettings Current { get; private set; } = current;

        public int SaveCount { get; private set; }

        public ValueTask<QsearchSettings> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Current);

        public ValueTask SaveAsync(QsearchSettings settings, CancellationToken cancellationToken)
        {
            SaveCount++;
            Current = settings;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ControlledPreviewService : IFilePreviewService
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _started = [];
        private readonly ConcurrentDictionary<string, TaskCompletionSource<FilePreviewData>> _pending = [];

        public ValueTask<FilePreviewData> LoadAsync(string path, CancellationToken cancellationToken)
        {
            string name = Path.GetFileName(path);
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<FilePreviewData> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _started[name] = started;
            _pending[name] = pending;
            started.TrySetResult();
            return new ValueTask<FilePreviewData>(pending.Task);
        }

        public Task Started(string name) => _started[name].Task;

        public void Complete(string name)
        {
            string path = Path.Combine(@"C:\scope", name);
            _pending[name].TrySetResult(new FilePreviewData(
                name,
                path,
                Path.GetDirectoryName(path)!,
                Path.GetExtension(path),
                false,
                1,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                FileAttributes.Normal,
                LocalPreviewKind.Text,
                name,
                null,
                false,
                null));
        }
    }

    private static bool ReadBoolean(QsearchSettings settings, string propertyName)
    {
        System.Reflection.PropertyInfo? property = settings.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<bool>(property.GetValue(settings));
    }

    private sealed class StubMachineIndexingService(MachineIndexStartResult start) : IMachineIndexingService
    {
        public IndexingSnapshot Snapshot { get; } = new(0, 0, 0, IndexFreshness.Unavailable, null, []);
        public Task Completion => Task.CompletedTask;
        public event Action<IndexingSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public ValueTask<MachineIndexStartResult> StartAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(start);
        public ValueTask<MachineIndexStartResult> RetryElevationAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(start);
        public ValueTask RebuildAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubShellActions : IShellActionService
    {
        public string? RevealedPath { get; private set; }

        public string? OpenedPath { get; private set; }

        public ValueTask OpenAsync(string path, CancellationToken cancellationToken)
        {
            OpenedPath = path;
            return ValueTask.CompletedTask;
        }

        public ValueTask RevealAsync(string path, CancellationToken cancellationToken)
        {
            RevealedPath = path;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingShellActions : IShellActionService
    {
        public ValueTask OpenAsync(string path, CancellationToken cancellationToken) =>
            ValueTask.FromException(new System.ComponentModel.Win32Exception("blocked"));

        public ValueTask RevealAsync(string path, CancellationToken cancellationToken) =>
            ValueTask.FromException(new System.ComponentModel.Win32Exception("blocked"));
    }

    private sealed class BlockingShellActions : IShellActionService
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public int OpenCalls { get; private set; }

        public async ValueTask OpenAsync(string path, CancellationToken cancellationToken)
        {
            OpenCalls++;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        public ValueTask RevealAsync(string path, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubReviewService(ReviewState state = ReviewState.NoNotableSignals) : IFileReviewService
    {
        public ValueTask<FileReviewResult> ReviewAsync(string path, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(
                new FileReviewResult(
                    state,
                    state == ReviewState.HighCaution ? 70 : state == ReviewState.ReviewSuggested ? 25 : 0,
                    FileKind.Unknown,
                    [],
                    true));
        }
    }

    private sealed class ControlledReviewService : IFileReviewService
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _started = [];
        private readonly ConcurrentDictionary<string, TaskCompletionSource<FileReviewResult>> _pending = [];

        public ValueTask<FileReviewResult> ReviewAsync(string path, CancellationToken cancellationToken)
        {
            string name = Path.GetFileName(path);
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<FileReviewResult> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _started[name] = started;
            _pending[name] = pending;
            started.TrySetResult();
            return new ValueTask<FileReviewResult>(pending.Task);
        }

        public Task Started(string name) => _started[name].Task;

        public void Complete(string name, ReviewState state)
        {
            _pending[name].TrySetResult(new FileReviewResult(
                state,
                state == ReviewState.ReviewSuggested ? 25 : 0,
                FileKind.Unknown,
                [new ReviewEvidence("test", 0, "Local observation", "Review its source")],
                true));
        }
    }

    private sealed class ThrowingReviewService : IFileReviewService
    {
        public ValueTask<FileReviewResult> ReviewAsync(string path, CancellationToken cancellationToken) =>
            ValueTask.FromException<FileReviewResult>(new IOException("unavailable"));
    }

    private sealed class ThrowingPreviewService : IFilePreviewService
    {
        public ValueTask<FilePreviewData> LoadAsync(string path, CancellationToken cancellationToken) =>
            ValueTask.FromException<FilePreviewData>(new IOException("unavailable"));
    }

    private sealed class MetadataOnlyPreviewService : IFilePreviewService
    {
        public ValueTask<FilePreviewData> LoadAsync(string path, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new FilePreviewData(
                Path.GetFileName(path),
                path,
                Path.GetDirectoryName(path)!,
                Path.GetExtension(path),
                false,
                42,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddDays(1),
                DateTimeOffset.UnixEpoch,
                FileAttributes.Archive,
                LocalPreviewKind.Metadata,
                null,
                null,
                false,
                "preview-unavailable"));
    }

    private sealed class StubDbException : DbException
    {
        public StubDbException()
            : base("busy")
        {
        }
    }
}
