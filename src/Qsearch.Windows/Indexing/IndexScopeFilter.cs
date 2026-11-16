using Qsearch.Core.Indexing;
using Qsearch.Core.Text;
using Qsearch.Windows.FileSystem;

namespace Qsearch.Windows.Indexing;

public sealed class IndexScopeFilter
{
    private readonly IndexScope _scope;

    public IndexScopeFilter(IndexScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    public bool ShouldInclude(FileSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!IsWithinScope(entry.FullPath) || IsExcludedSubtree(entry.FullPath))
        {
            return false;
        }

        if (!_scope.IncludeHidden && (entry.Attributes & FileAttributes.Hidden) != 0)
        {
            return false;
        }

        if (!_scope.IncludeSystem && (entry.Attributes & FileAttributes.System) != 0)
        {
            return false;
        }

        if (!entry.IsDirectory && _scope.ExcludedExtensions is not null)
        {
            string extension = SearchTextNormalizer.Normalize(
                Path.GetExtension(entry.Name).TrimStart('.'));
            if (_scope.ExcludedExtensions.Any(
                    excluded => string.Equals(
                        SearchTextNormalizer.Normalize(excluded.TrimStart('.')),
                        extension,
                        StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsWithinScope(string path)
    {
        string root = _scope.RootPath.TrimEnd('\\', '/');
        string candidate = path.TrimEnd('\\', '/');
        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    public bool ShouldRecurse(FileSystemEntry entry)
    {
        return entry.IsDirectory &&
            (entry.Attributes & FileAttributes.ReparsePoint) == 0 &&
            ShouldInclude(entry);
    }

    private bool IsExcludedSubtree(string path)
    {
        if (_scope.ExcludedSubtrees is null)
        {
            return false;
        }

        foreach (string excluded in _scope.ExcludedSubtrees)
        {
            string root = excluded.TrimEnd('\\', '/');
            if (string.Equals(path.TrimEnd('\\', '/'), root, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
