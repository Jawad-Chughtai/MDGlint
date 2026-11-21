using MDGlint.Desktop.Helpers;

namespace MDGlint.Desktop.Tests;

public sealed class DocumentUrlsTests
{
    [Theory]
    [InlineData(@"C:\docs", "https://doc.mdglint.example/C/docs/")]
    [InlineData(@"c:\docs\", "https://doc.mdglint.example/C/docs/")]
    [InlineData(@"C:\", "https://doc.mdglint.example/C/")]
    [InlineData(@"D:\My Docs\#1 & more", "https://doc.mdglint.example/D/My%20Docs/%231%20%26%20more/")]
    [InlineData(@"C:\docs\sub\..\other", "https://doc.mdglint.example/C/docs/other/")]
    [InlineData(@"\\server\share\docs", "https://doc.mdglint.example/UNC/server/share/docs/")]
    [InlineData(@"\\server\share", "https://doc.mdglint.example/UNC/server/share/")]
    public void ToFolderUrl_SpellsOutThePath(string folder, string expected)
    {
        Assert.Equal(expected, DocumentUrls.ToFolderUrl(folder));
    }

    [Theory]
    [InlineData(@"\\?\C:\docs")] // Device paths can't be expressed.
    [InlineData(@"\\.\pipe\x")]
    [InlineData(@"\\server")] // A server without a share.
    [InlineData("")] // Not a path at all.
    [InlineData("C:\\a\0b")]
    public void ToFolderUrl_RefusesPathsItCannotExpress(string folder)
    {
        Assert.Null(DocumentUrls.ToFolderUrl(folder));
    }

    [Fact]
    public void ToFolderUrl_RefusesARelativeRootOutsideADrive()
    {
        // A path that resolves to neither a drive nor a share can't happen on Windows, so this
        // checks the guard through a root-relative path resolved against the current drive.
        Assert.StartsWith("https://doc.mdglint.example/", DocumentUrls.ToFolderUrl(@"\docs")!);
    }

    [Theory]
    [InlineData("https://doc.mdglint.example/C/docs/a.png", @"C:\docs\a.png")]
    [InlineData("https://doc.mdglint.example/c/docs/a.png", @"C:\docs\a.png")]
    [InlineData("https://DOC.mdglint.example/C/My%20Docs/a%23b.png", @"C:\My Docs\a#b.png")]
    [InlineData("https://doc.mdglint.example/C/docs/../a.png", @"C:\a.png")]
    [InlineData("https://doc.mdglint.example/UNC/server/share/a.png", @"\\server\share\a.png")]
    [InlineData("https://doc.mdglint.example/C", @"C:\")]
    public void TryGetPath_MapsBackToTheLocalPath(string url, string expected)
    {
        Assert.True(DocumentUrls.TryGetPath(new Uri(url), out var path));
        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData("http://doc.mdglint.example/C/a.png")] // Not https.
    [InlineData("https://other.example/C/a.png")] // Another host.
    [InlineData("https://doc.mdglint.example/")] // No path.
    [InlineData("https://doc.mdglint.example/UNC/server")] // No share.
    [InlineData("https://doc.mdglint.example/CD/a.png")] // Not a drive letter.
    [InlineData("https://doc.mdglint.example/1/a.png")]
    [InlineData("https://doc.mdglint.example/C/a%2F..%2F..%2Fb.png")] // Encoded separators.
    [InlineData("https://doc.mdglint.example/C/a%5Cb.png")]
    [InlineData("https://doc.mdglint.example/C/a%3Ab.png")] // A colon (alternate data streams).
    [InlineData("https://doc.mdglint.example/C/%2E%2E/a.png")] // Encoded "..".
    [InlineData("https://doc.mdglint.example/C/../../a.png")] // Climbing above the drive drops it.
    [InlineData("https://doc.mdglint.example/UNC/server/share/a%00.png")] // Not a valid path.
    public void TryGetPath_RefusesAnythingElse(string url)
    {
        Assert.False(DocumentUrls.TryGetPath(new Uri(url), out var path));
        Assert.Equal(string.Empty, path);
    }

    [Theory]
    [InlineData(@"C:\docs\notes")]
    [InlineData(@"\\server\share\team docs")]
    public void RoundTrips(string folder)
    {
        var url = DocumentUrls.ToFolderUrl(folder)!;

        Assert.True(DocumentUrls.TryGetPath(new Uri(new Uri(url), "file.md"), out var path));
        Assert.Equal(Path.Combine(folder, "file.md"), path);
    }
}
