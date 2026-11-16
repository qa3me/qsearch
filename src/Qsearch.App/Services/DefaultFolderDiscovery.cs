using System.IO;

namespace Qsearch.App.Services;

public interface IDefaultFolderDiscovery
{
    IReadOnlyList<string> Discover();
}

public sealed class DefaultFolderDiscovery : IDefaultFolderDiscovery
{
    public IReadOnlyList<string> Discover()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return DefaultFolderSet.Normalize(
            [
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(profile, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            ]);
    }
}

public static class DefaultFolderSet
{
    public static IReadOnlyList<string> Normalize(IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        List<string> result = [];
        foreach (string path in candidates
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Where(Directory.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(path => path.Length)
                     .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (result.Any(parent => IsSameOrNested(path, parent)))
            {
                continue;
            }

            result.Add(path);
        }

        return result;
    }

    private static bool IsSameOrNested(string path, string parent)
    {
        if (string.Equals(path, parent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string prefix = Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
