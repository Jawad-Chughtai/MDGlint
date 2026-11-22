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
    public void Actions_MatchEachCase()
    {
        Assert.Equal(PreviewNoticeAction.DownloadRuntime, PreviewUnavailableNotice.RuntimeMissing.Action);
        Assert.Equal(PreviewNoticeAction.DownloadRuntime, PreviewUnavailableNotice.StartFailed("x").Action);
        Assert.Equal(PreviewNoticeAction.EndStuckPreview, PreviewUnavailableNotice.Stuck.Action);
        Assert.Equal(PreviewNoticeAction.None, PreviewUnavailableNotice.SharedPreviewNotAnswering.Action);
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    [InlineData(false, 0, false)]
    [InlineData(false, 2, false)]
    public void BeforeStart_IsStuckOnlyWhenNoWindowOwnsTheHeldFolder(bool held, int otherWindows, bool stuck)
    {
        Assert.Equal(stuck ? PreviewUnavailableNotice.Stuck : null, PreviewUnavailableNotice.BeforeStart(held, otherWindows));
    }

    [Fact]
    public void ForTimeout_PicksTheNoticeThatSaysWhatToDo()
    {
        Assert.Same(PreviewUnavailableNotice.Stuck, PreviewUnavailableNotice.ForTimeout(true, 0, "timeout"));
        Assert.Same(PreviewUnavailableNotice.Stuck, PreviewUnavailableNotice.ForTimeout(true, 1, "timeout"));
        Assert.Same(PreviewUnavailableNotice.SharedPreviewNotAnswering, PreviewUnavailableNotice.ForTimeout(false, 1, "timeout"));
        Assert.Equal(PreviewUnavailableNotice.StartFailed("timeout"), PreviewUnavailableNotice.ForTimeout(false, 0, "timeout"));
    }

    [Fact]
    public void TimeoutHResult_IsErrorTimeout()
    {
        Assert.Equal("800705B4", PreviewUnavailableNotice.TimeoutHResult.ToString("X8"));
    }

    [Fact]
    public void RuntimeDownloadUrl_IsMicrosoftsHttpsPage()
    {
        var url = new Uri(PreviewUnavailableNotice.RuntimeDownloadUrl);

        Assert.Equal(Uri.UriSchemeHttps, url.Scheme);
        Assert.Equal("developer.microsoft.com", url.Host);
    }
}
