using System.Diagnostics;

namespace Qsearch.Windows.Shell;

public interface IExternalLinkService
{
    void OpenHttps(Uri uri);
}

public interface IExternalLinkPlatform
{
    void Open(Uri uri);
}

public sealed class ExternalLinkService : IExternalLinkService
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "qa3.me",
        "qsearch.qa3.me",
    };
    private readonly IExternalLinkPlatform _platform;

    public ExternalLinkService(IExternalLinkPlatform? platform = null)
    {
        _platform = platform ?? new ShellExternalLinkPlatform();
    }

    public void OpenHttps(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "Only verified Qsearch HTTPS links are allowed.",
                nameof(uri));
        }

        bool hasVerifiedOrigin = AllowedHosts.Contains(uri.IdnHost) || IsQsearchGitHubRepository(uri);
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !hasVerifiedOrigin ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException(
                "Only verified Qsearch HTTPS links are allowed.",
                nameof(uri));
        }

        _platform.Open(uri);
    }

    private static bool IsQsearchGitHubRepository(Uri uri)
    {
        return string.Equals(uri.IdnHost, "github.com", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(uri.AbsolutePath.TrimEnd('/'), "/qa3me/qsearch", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment);
    }
}

public sealed class ShellExternalLinkPlatform : IExternalLinkPlatform
{
    public void Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        _ = Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true,
        });
    }
}
