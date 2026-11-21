using MDGlint.Core.Models;

namespace MDGlint.Core.Tests;

public sealed class PreviewUnavailableNoticeTests
{
    [Fact]
    public void RuntimeMissing_NamesTheRuntimeAndSaysWhatToDo()
    {
        var notice = PreviewUnavailableNotice.RuntimeMissing;

        Assert.Contains("Microsoft Edge WebView2 Runtime", notice.Title);
        Assert.Contains("isn't installed", notice.Message);
        Assert.Contains("open MDGlint again", notice.Message);
    }

    [Fact]
    public void StartFailed_IncludesTheError()
    {
        var notice = PreviewUnavailableNotice.StartFailed("  Access is denied.  ");

        Assert.Equal("The preview couldn't start", notice.Title);
        Assert.Contains("installed but didn't start", notice.Message);
        Assert.EndsWith("\n\nError: Access is denied.", notice.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StartFailed_WithoutAnError_LeavesOutTheErrorLine(string? reason)
    {
        var notice = PreviewUnavailableNotice.StartFailed(reason);

        Assert.DoesNotContain("Error:", notice.Message);
        Assert.EndsWith("usually fixes this.", notice.Message);
    }

    [Fact]
    public void RuntimeDownloadUrl_IsMicrosoftsHttpsPage()
    {
        var url = new Uri(PreviewUnavailableNotice.RuntimeDownloadUrl);

        Assert.Equal(Uri.UriSchemeHttps, url.Scheme);
        Assert.Equal("developer.microsoft.com", url.Host);
    }
}
