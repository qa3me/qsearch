namespace Qsearch.App.ViewModels;

public sealed class AboutViewModel
{
    public AboutViewModel()
    {
        Version = typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ??
            "0.1.0";
    }

    public string ProductName { get; } = "Qsearch";

    public string CreatorName { get; } = "Behzad Qasemi";

    public string BrandName { get; } = "QA3";

    public string Version { get; }

    public Uri DeveloperUri { get; } = new("https://qa3.me/", UriKind.Absolute);

    public Uri ProductUri { get; } = new("https://qsearch.qa3.me/", UriKind.Absolute);

    public Uri GitHubUri { get; } = new("https://github.com/qa3me/qsearch", UriKind.Absolute);
}
