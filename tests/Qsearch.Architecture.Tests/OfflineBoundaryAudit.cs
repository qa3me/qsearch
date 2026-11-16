using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Qsearch.Architecture.Tests;

public sealed record OfflineBoundaryResult(
    IReadOnlyList<string> NetworkAssemblyReferences,
    IReadOnlyList<string> RemoteEndpoints,
    IReadOnlyList<string> TelemetryPackages);

public static partial class OfflineBoundaryAudit
{
    private static readonly HashSet<string> NetworkClientAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Net.Http",
        "System.Net.Mail",
        "System.Net.Quic",
        "System.Net.Sockets",
        "System.Net.WebSockets",
    };

    public static OfflineBoundaryResult Scan(
        IEnumerable<Assembly> assemblies,
        string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        string[] networkReferences = assemblies
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(reference => reference.Name ?? string.Empty)
            .Where(NetworkClientAssemblies.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        List<string> endpoints = [];
        string sourceRoot = Path.Combine(repositoryRoot, "src");
        foreach (string path in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                     .Where(IsAuditedSource))
        {
            int lineNumber = 0;
            foreach (string line in File.ReadLines(path))
            {
                lineNumber++;
                foreach (Match match in EndpointPattern().Matches(line))
                {
                    if (!IsAllowedNonRuntimeEndpoint(match.Value))
                    {
                        endpoints.Add($"{Path.GetRelativePath(repositoryRoot, path)}:{lineNumber}:{match.Value}");
                    }
                }
            }
        }

        string[] telemetryPackages = Directory
            .EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path).Descendants("PackageReference"))
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(package => TelemetryPackagePattern().IsMatch(package))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new OfflineBoundaryResult(networkReferences, endpoints, telemetryPackages);
    }

    private static bool IsAuditedSource(string path)
    {
        if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Path.GetExtension(path) is ".cs" or ".xaml" or ".csproj";
    }

    private static bool IsAllowedNonRuntimeEndpoint(string endpoint)
    {
        return endpoint.StartsWith("http://schemas.microsoft.com/", StringComparison.OrdinalIgnoreCase) ||
            endpoint.StartsWith("http://schemas.openxmlformats.org/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(endpoint, "https://qa3.me/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(endpoint, "https://qsearch.qa3.me/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(endpoint, "https://github.com/qa3me/qsearch", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("https?://[^\\s\\\"'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EndpointPattern();

    [GeneratedRegex("(telemetry|applicationinsights|opentelemetry|appcenter|sentry)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TelemetryPackagePattern();
}

public static class RepositoryRoot
{
    public static string Find()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Qsearch.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("The Qsearch repository root was not found.");
    }
}
