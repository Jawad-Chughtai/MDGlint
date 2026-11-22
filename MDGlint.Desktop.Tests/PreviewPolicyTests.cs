using MDGlint.Desktop.Helpers;

namespace MDGlint.Desktop.Tests;

public sealed class PreviewPolicyTests
{
    [Theory]
    [InlineData("https://app.mdglint.example/index.html", true)]
    [InlineData("https://APP.mdglint.example/index.html?x#y", true)]
    [InlineData("http://app.mdglint.example/index.html", false)] // Not https.
    [InlineData("https://app.mdglint.example/other.html", false)]
    [InlineData("https://app.mdglint.example/INDEX.html", false)]
    [InlineData("https://doc.mdglint.example/index.html", false)]
    [InlineData("about:blank", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void IsShell_OnlyTheShellPage(string? address, bool expected)
    {
        Assert.Equal(expected, PreviewPolicy.IsShell(address));
    }

    [Theory]
    [InlineData("GET", "https://doc.mdglint.example/C/a.png", @"C:\a.png", "image/png")]
    [InlineData("get", "https://doc.mdglint.example/C/a.JPG", @"C:\a.JPG", "image/jpeg")]
    [InlineData("GET", "https://doc.mdglint.example/C/v/clip.webm", @"C:\v\clip.webm", "video/webm")]
    [InlineData("GET", "https://doc.mdglint.example/C/song.mp3", @"C:\song.mp3", "audio/mpeg")]
    public void TryGetLocalMedia_ServesImagesAudioAndVideo(string method, string url, string path, string type)
    {
        Assert.True(PreviewPolicy.TryGetLocalMedia(method, new Uri(url), out var actualPath, out var actualType));
        Assert.Equal(path, actualPath);
        Assert.Equal(type, actualType);
    }

    [Theory]
    [InlineData("POST", "https://doc.mdglint.example/C/a.png")]
    [InlineData("GET", "https://doc.mdglint.example/C/notes.md")] // Not media.
    [InlineData("GET", "https://doc.mdglint.example/C/secrets.txt")]
    [InlineData("GET", "https://doc.mdglint.example/C/page.html")]
    [InlineData("GET", "https://doc.mdglint.example/C/script.js")]
    [InlineData("GET", "https://doc.mdglint.example/C/noextension")]
    [InlineData("GET", "https://doc.mdglint.example/")] // No path.
    public void TryGetLocalMedia_RefusesEverythingElse(string method, string url)
    {
        Assert.False(PreviewPolicy.TryGetLocalMedia(method, new Uri(url), out _, out _));
    }

    [Theory]
    [InlineData("https://doc.mdglint.example/C/a.png", true)]
    [InlineData("https://DOC.MDGLINT.EXAMPLE/C/a.png", true)]
    [InlineData("https://app.mdglint.example/a.png", false)]
    public void IsDocumentHost(string url, bool expected)
    {
        Assert.Equal(expected, PreviewPolicy.IsDocumentHost(new Uri(url)));
    }

    [Theory]
    [InlineData("https://example.com/pixel.png", false, true)]
    [InlineData("http://example.com/pixel.png", false, true)]
    [InlineData("https://example.com/pixel.png", true, false)] // Allowed.
    [InlineData("https://app.mdglint.example/vendor/x.png", false, false)] // The app's own hosts.
    [InlineData("https://doc.mdglint.example/C/a.png", false, false)]
    [InlineData("data:image/png;base64,AAAA", false, false)]
    public void IsBlockedRemote(string url, bool allow, bool expected)
    {
        Assert.Equal(expected, PreviewPolicy.IsBlockedRemote(new Uri(url), allow));
    }

    [Theory]
    [InlineData("""{"type":"ready"}""", true)]
    [InlineData("""{"type":"ready","extra":1}""", true)]
    [InlineData("""{"type":"READY"}""", false)]
    [InlineData("""{"type":5}""", false)]
    [InlineData("""{"kind":"ready"}""", false)]
    [InlineData("""["ready"]""", false)]
    [InlineData("\"ready\"", false)]
    [InlineData("not json", false)]
    public void IsReadyMessage(string json, bool expected)
    {
        Assert.Equal(expected, PreviewPolicy.IsReadyMessage(json));
    }

    [Theory]
    [InlineData("""{"type":"drop"}""", true)]
    [InlineData("""{"type":"ready"}""", false)]
    [InlineData("""{"type":"DROP"}""", false)]
    [InlineData("""["drop"]""", false)]
    [InlineData("not json", false)]
    public void IsDropMessage(string json, bool expected)
    {
        Assert.Equal(expected, PreviewPolicy.IsDropMessage(json));
    }

    [Theory]
    [InlineData("""{"type":"open"}""", true)]
    [InlineData("""{"type":"drop"}""", false)]
    [InlineData("""{"type":"OPEN"}""", false)]
    [InlineData("""["open"]""", false)]
    [InlineData("not json", false)]
    public void IsOpenMessage(string json, bool expected)
    {
        Assert.Equal(expected, PreviewPolicy.IsOpenMessage(json));
    }

    [Fact]
    public void DroppedMarkdownFile_IsTheFirstMarkdownFile()
    {
        Assert.Equal(@"C:\docs\b.md", PreviewPolicy.DroppedMarkdownFile([@"C:\docs\a.png", null, " ", @"C:\docs\b.md", @"C:\docs\c.md"]));
        Assert.Equal(@"C:\docs\README.MARKDOWN", PreviewPolicy.DroppedMarkdownFile([@"C:\docs\README.MARKDOWN"]));
    }

    [Fact]
    public void DroppedMarkdownFile_IsNullWithoutOne()
    {
        Assert.Null(PreviewPolicy.DroppedMarkdownFile([@"C:\docs\a.png", @"C:\docs\notes.txt"]));
        Assert.Null(PreviewPolicy.DroppedMarkdownFile([]));
        Assert.Null(PreviewPolicy.DroppedMarkdownFile(null));
    }

    [Theory]
    [InlineData("https://example.com/page", "https://example.com/page")]
    [InlineData("http://example.com/", "http://example.com/")]
    [InlineData("mailto:someone@example.com", "mailto:someone@example.com")]
    public void ForLink_OpensWebLinksInTheBrowser(string link, string target)
    {
        Assert.Equal(new LinkAction(LinkActionKind.OpenInBrowser, target), PreviewPolicy.ForLink(link, _ => false, _ => false));
    }

    [Theory]
    [InlineData("file:///C:/Windows/notepad.exe")] // Other schemes never run.
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("https://app.mdglint.example/index.html#x")] // The app's own page.
    [InlineData("#heading")] // Not absolute: in-page links are handled by the page.
    [InlineData(null)]
    [InlineData("https://doc.mdglint.example/")] // Not a local path.
    public void ForLink_IgnoresEverythingElse(string? link)
    {
        Assert.Equal(LinkAction.Ignore, PreviewPolicy.ForLink(link, _ => true, _ => true));
    }

    [Theory]
    [InlineData("https://doc.mdglint.example/C/docs/other.md", @"C:\docs\other.md")]
    [InlineData("https://doc.mdglint.example/C/docs/other.MARKDOWN", @"C:\docs\other.MARKDOWN")]
    public void ForLink_OpensLocalMarkdownInMDGlint(string link, string path)
    {
        Assert.Equal(new LinkAction(LinkActionKind.OpenInMDGlint, path), PreviewPolicy.ForLink(link, p => p == path, _ => false));
    }

    [Fact]
    public void ForLink_ShowsOtherLocalFilesAndFoldersInExplorer()
    {
        const string Exe = @"C:\tools\setup.exe";
        const string Folder = @"C:\tools";

        Assert.Equal(
            new LinkAction(LinkActionKind.ShowInExplorer, Exe),
            PreviewPolicy.ForLink("https://doc.mdglint.example/C/tools/setup.exe", p => p == Exe, _ => false));
        Assert.Equal(
            new LinkAction(LinkActionKind.ShowInExplorer, Folder),
            PreviewPolicy.ForLink("https://doc.mdglint.example/C/tools", _ => false, p => p == Folder));
    }

    [Fact]
    public void ForLink_IgnoresAMissingLocalFile()
    {
        Assert.Equal(LinkAction.Ignore, PreviewPolicy.ForLink("https://doc.mdglint.example/C/missing.md", _ => false, _ => false));
    }

    [Fact]
    public void ForLink_ChecksTheRealFileSystemByDefault()
    {
        var file = Path.Combine(Path.GetTempPath(), $"mdglint-{Guid.NewGuid():N}.md");
        File.WriteAllText(file, "# Test");
        try
        {
            var url = new Uri(new Uri(DocumentUrls.ToFolderUrl(Path.GetDirectoryName(file)!)!), Path.GetFileName(file));

            Assert.Equal(LinkActionKind.OpenInMDGlint, PreviewPolicy.ForLink(url.AbsoluteUri).Kind);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
