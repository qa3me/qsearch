using Qsearch.Core.Text;

namespace Qsearch.Safety;

public sealed record ReviewCacheKey(
    string VolumeIdentity,
    string FileIdentity,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    int PolicyVersion,
    bool HasStableIdentity)
{
    public static ReviewCacheKey FromStableIdentity(
        string volumeIdentity,
        string fileIdentity,
        long size,
        DateTimeOffset lastWriteTimeUtc,
        int policyVersion)
    {
        return new ReviewCacheKey(
            volumeIdentity,
            fileIdentity,
            size,
            lastWriteTimeUtc,
            policyVersion,
            true);
    }

    public static ReviewCacheKey FromPathFallback(
        string path,
        long size,
        DateTimeOffset lastWriteTimeUtc,
        int policyVersion)
    {
        return new ReviewCacheKey(
            "path-fallback",
            SearchTextNormalizer.Normalize(path),
            size,
            lastWriteTimeUtc,
            policyVersion,
            false);
    }
}
