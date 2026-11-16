using Qsearch.Windows.Shell;

namespace Qsearch.Windows.Tests;

public sealed class ExternalLinkServiceTests
{
    [Theory]
    [InlineData("https://qa3.me/")]
    [InlineData("https://qa3.me/about?q=qsearch#work")]
    [InlineData("https://qsearch.qa3.me/")]
    [InlineData("https://github.com/qa3me/qsearch")]
    [InlineData("https://github.com/qa3me/qsearch/")]
    public void OpenHttps_AllowsOnlyVerifiedQa3Origins(string address)
    {
        RecordingLinkPlatform platform = new();
        ExternalLinkService service = new(platform);
        Uri uri = new(address);

        service.OpenHttps(uri);

        Assert.Equal(uri, platform.OpenedUri);
    }

    [Theory]
    [InlineData("http://qa3.me/")]
    [InlineData("https://www.qa3.me/")]
    [InlineData("https://preview.qsearch.qa3.me/")]
    [InlineData("https://qa3.me.evil.example/")]
    [InlineData("https://user@qa3.me/")]
    [InlineData("https://qa3.me:444/")]
    [InlineData("https://github.com/qa3me/other")]
    [InlineData("https://github.com/other/qsearch")]
    [InlineData("https://github.com/qa3me/qsearch/issues")]
    [InlineData("https://github.com/qa3me/qsearch?tab=readme")]
    [InlineData("https://github.com/qa3me/qsearch#readme")]
    [InlineData("https://user@github.com/qa3me/qsearch")]
    [InlineData("file:///C:/Users/example.txt")]
    public void OpenHttps_RejectsEveryOtherOrigin(string address)
    {
        RecordingLinkPlatform platform = new();
        ExternalLinkService service = new(platform);

        Assert.Throws<ArgumentException>(() => service.OpenHttps(new Uri(address)));
        Assert.Null(platform.OpenedUri);
    }

    [Fact]
    public void OpenHttps_RejectsRelativeUrisWithTheSameControlledException()
    {
        RecordingLinkPlatform platform = new();
        ExternalLinkService service = new(platform);

        Assert.Throws<ArgumentException>(() => service.OpenHttps(new Uri("qa3me/qsearch", UriKind.Relative)));
        Assert.Null(platform.OpenedUri);
    }

    private sealed class RecordingLinkPlatform : IExternalLinkPlatform
    {
        public Uri? OpenedUri { get; private set; }

        public void Open(Uri uri)
        {
            OpenedUri = uri;
        }
    }
}
