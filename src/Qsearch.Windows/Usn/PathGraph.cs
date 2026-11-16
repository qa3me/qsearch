namespace Qsearch.Windows.Usn;

public sealed class PathGraph
{
    private const int MaximumParentDepth = 4096;
    private readonly Dictionary<FileReferenceId, UsnRecord> _records = [];
    private readonly string _volumeRoot;

    public PathGraph(string volumeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeRoot);
        _volumeRoot = volumeRoot.TrimEnd('\\', '/') + "\\";
    }

    public IReadOnlyCollection<UsnRecord> Records => _records.Values;

    public void Apply(UsnRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records[record.FileReferenceNumber] = record;
    }

    public void Remove(FileReferenceId reference)
    {
        _records.Remove(reference);
    }

    public bool TryResolvePath(FileReferenceId reference, out string path)
    {
        List<string> segments = [];
        HashSet<FileReferenceId> visited = [];
        FileReferenceId current = reference;

        for (int depth = 0; depth < MaximumParentDepth; depth++)
        {
            if (!visited.Add(current) || !_records.TryGetValue(current, out UsnRecord? record))
            {
                path = string.Empty;
                return false;
            }

            if (!string.IsNullOrEmpty(record.Name))
            {
                segments.Add(record.Name);
            }

            if (record.ParentFileReferenceNumber == current)
            {
                segments.Reverse();
                path = segments.Count == 0
                    ? _volumeRoot
                    : _volumeRoot + string.Join("\\", segments);
                return true;
            }

            current = record.ParentFileReferenceNumber;
        }

        path = string.Empty;
        return false;
    }
}
