using MDGlint.Desktop.Helpers;

namespace MDGlint.Desktop.Tests;

public sealed class PreviewProcessTests
{
    private const string Folder = @"C:\Users\me\AppData\Local\MDGlint\WebView2";

    [Theory]
    [InlineData(@"""C:\EdgeWebView\msedgewebview2.exe"" --embedded-browser-webview=1 --user-data-dir=""C:\Users\me\AppData\Local\MDGlint\WebView2\EBWebView"" --noerrdialogs", true)]
    [InlineData(@"msedgewebview2.exe --user-data-dir=C:\Users\me\AppData\Local\MDGlint\WebView2\EBWebView --lang=en", true)]
    [InlineData(@"msedgewebview2.exe --user-data-dir=""c:\users\ME\appdata\local\mdglint\webview2\ebwebview\""", true)]
    [InlineData(@"msedgewebview2.exe --type=renderer --user-data-dir=""C:\Users\me\AppData\Local\MDGlint\WebView2\EBWebView""", false)]
    [InlineData(@"msedgewebview2.exe --user-data-dir=""C:\Users\me\AppData\Local\Other\EBWebView""", false)]
    [InlineData(@"msedgewebview2.exe --user-data-dir=""C:\Users\me\AppData\Local\MDGlint\WebView2\EBWebView2""", false)]
    [InlineData(@"msedgewebview2.exe --embedded-browser-webview=1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBrowserFor_MatchesOnlyTheBrowserProcessForThisFolder(string? commandLine, bool expected)
    {
        Assert.Equal(expected, PreviewProcess.IsBrowserFor(commandLine, Folder));
    }

    [Fact]
    public void IsOwnerGone_OnlyWhileTheMDGlintThatStartedItRuns()
    {
        var browser = new DateTime(2026, 11, 22, 15, 23, 27);

        Assert.False(PreviewProcess.IsOwnerGone("MDGlint.Desktop", browser.AddSeconds(-2), "MDGlint.Desktop", browser));
        Assert.False(PreviewProcess.IsOwnerGone("mdglint.desktop", browser, "MDGlint.Desktop", browser));
        Assert.True(PreviewProcess.IsOwnerGone(null, null, "MDGlint.Desktop", browser));
        Assert.True(PreviewProcess.IsOwnerGone("explorer", browser.AddHours(-1), "MDGlint.Desktop", browser));
        Assert.True(PreviewProcess.IsOwnerGone("MDGlint.Desktop", null, "MDGlint.Desktop", browser));
        // The ID now belongs to a newer MDGlint, not the one that started the browser.
        Assert.True(PreviewProcess.IsOwnerGone("MDGlint.Desktop", browser.AddMinutes(20), "MDGlint.Desktop", browser));
    }
}
