using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Qsearch.App.Services;

namespace Qsearch.App.ViewModels;

public enum WidgetFilter
{
    All,
    Files,
    Folders,
}

public sealed class WidgetViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MainViewModel _main;
    private WidgetFilter _filter;

    public WidgetViewModel(MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);
        _main = main;
        _main.PropertyChanged += OnMainPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SearchText
    {
        get => RemoveTypeFilters(_main.SearchText);
        set
        {
            _main.SearchText = ApplyFilter(value);
            OnPropertyChanged();
        }
    }

    public ObservableCollection<ResultItemViewModel> Results => _main.Results;

    public IndexingSnapshot? IndexingSnapshot => _main.IndexingSnapshot;

    public IndexStartupState IndexStartupState => _main.IndexStartupState;

    public WidgetFilter Filter
    {
        get => _filter;
        private set
        {
            if (_filter == value)
            {
                return;
            }

            _filter = value;
            OnPropertyChanged();
        }
    }

    public ResultItemViewModel? SelectedResult
    {
        get => _main.SelectedResult;
        set
        {
            _main.SelectedResult = value;
            OnPropertyChanged();
        }
    }

    public ValueTask SearchAsync(string text) => _main.SetSearchTextAsync(ApplyFilter(text));

    public ValueTask ApplyFilterAsync(WidgetFilter filter)
    {
        string visibleSearchText = SearchText;
        Filter = filter;
        return SearchAsync(visibleSearchText);
    }

    public ValueTask OpenAsync(CancellationToken cancellationToken) => _main.OpenAsync(cancellationToken);

    public void Dispose()
    {
        _main.PropertyChanged -= OnMainPropertyChanged;
        GC.SuppressFinalize(this);
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SearchText) or
            nameof(MainViewModel.SelectedResult) or
            nameof(MainViewModel.IndexingSnapshot) or
            nameof(MainViewModel.IndexStartupState))
        {
            OnPropertyChanged(e.PropertyName);
        }
    }

    private string ApplyFilter(string text)
    {
        string withoutType = RemoveTypeFilters(text);
        string prefix = Filter switch
        {
            WidgetFilter.Files => "type:file",
            WidgetFilter.Folders => "type:folder",
            _ => string.Empty,
        };
        return string.IsNullOrEmpty(prefix)
            ? withoutType
            : string.IsNullOrEmpty(withoutType)
                ? prefix
                : $"{prefix} {withoutType}";
    }

    private static string RemoveTypeFilters(string text)
    {
        return string.Join(
            ' ',
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => !token.StartsWith("type:", StringComparison.OrdinalIgnoreCase)));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
