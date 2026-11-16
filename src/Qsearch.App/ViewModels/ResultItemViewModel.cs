using System.ComponentModel;
using System.Runtime.CompilerServices;
using Qsearch.Core.Indexing;

namespace Qsearch.App.ViewModels;

public sealed class ResultItemViewModel : INotifyPropertyChanged
{
    private static readonly Dictionary<char, string> DirectionalControls = new()
    {
        ['\u202A'] = "LRE",
        ['\u202B'] = "RLE",
        ['\u202C'] = "PDF",
        ['\u202D'] = "LRO",
        ['\u202E'] = "RLO",
        ['\u2066'] = "LRI",
        ['\u2067'] = "RLI",
        ['\u2068'] = "FSI",
        ['\u2069'] = "PDI",
    };

    public ResultItemViewModel(SearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Id = result.Id;
        _name = result.Name;
        _displayName = NeutralizeDirectionalControls(result.Name);
        _fullPath = result.FullPath;
        _type = result.Type;
        _size = result.Size;
        _lastWriteTimeUtc = result.LastWriteTimeUtc;
        _visualKind = ResultVisualClassifier.Classify(result.Name, result.Type);
    }

    private string _name;
    private string _displayName;
    private string _fullPath;
    private IndexItemType _type;
    private long _size;
    private DateTimeOffset _lastWriteTimeUtc;
    private ResultVisualKind _visualKind;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Name => _name;

    public string DisplayName => _displayName;

    public string FullPath => _fullPath;

    public IndexItemType Type => _type;

    public bool IsFolder => _type == IndexItemType.Folder;

    public long Size => _size;

    public DateTimeOffset LastWriteTimeUtc => _lastWriteTimeUtc;

    public ResultVisualKind VisualKind => _visualKind;

    public bool IsApplication => _visualKind == ResultVisualKind.Application;

    public bool UpdateFrom(SearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!string.Equals(Id, result.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException("The stable item ID cannot change.", nameof(result));
        }

        bool selectionDetailsChanged =
            !string.Equals(_fullPath, result.FullPath, StringComparison.Ordinal) ||
            _type != result.Type ||
            _size != result.Size ||
            _lastWriteTimeUtc != result.LastWriteTimeUtc;
        SetField(ref _name, result.Name, nameof(Name));
        SetField(ref _displayName, NeutralizeDirectionalControls(result.Name), nameof(DisplayName));
        SetField(ref _fullPath, result.FullPath, nameof(FullPath));
        if (_type != result.Type)
        {
            _type = result.Type;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFolder)));
        }
        SetField(ref _size, result.Size, nameof(Size));
        SetField(ref _lastWriteTimeUtc, result.LastWriteTimeUtc, nameof(LastWriteTimeUtc));
        ResultVisualKind nextVisualKind = ResultVisualClassifier.Classify(result.Name, result.Type);
        if (_visualKind != nextVisualKind)
        {
            _visualKind = nextVisualKind;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisualKind)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsApplication)));
        }
        return selectionDetailsChanged;
    }

    public override string ToString() => DisplayName;

    private static string NeutralizeDirectionalControls(string value)
    {
        if (!value.Any(DirectionalControls.ContainsKey))
        {
            return value;
        }

        System.Text.StringBuilder result = new(value.Length + 8);
        foreach (char character in value)
        {
            if (DirectionalControls.TryGetValue(character, out string? name))
            {
                result.Append('⟦').Append(name).Append('⟧');
            }
            else
            {
                result.Append(character);
            }
        }

        return result.ToString();
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum ResultVisualKind
{
    File,
    Folder,
    Application,
}

public static class ResultVisualClassifier
{
    private static readonly HashSet<string> ApplicationExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".appref-ms",
        ".bat",
        ".cmd",
        ".com",
        ".exe",
        ".lnk",
        ".msi",
        ".msc",
        ".ps1",
    };

    public static ResultVisualKind Classify(string name, IndexItemType type)
    {
        if (type == IndexItemType.Folder)
        {
            return ResultVisualKind.Folder;
        }

        string extension = System.IO.Path.GetExtension(name);
        return ApplicationExtensions.Contains(extension)
            ? ResultVisualKind.Application
            : ResultVisualKind.File;
    }
}
