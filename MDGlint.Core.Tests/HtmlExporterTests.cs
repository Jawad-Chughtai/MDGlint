using MDGlint.Core.Models;
using MDGlint.Core.Services;

namespace MDGlint.Core.Tests;

public class HtmlExporterTests
{
    private const string Png = "data:image/png;base64,iVBORw0KGgo=";

    private static Task<string> Build(string content, Func<Uri, Task<string?>>? embed = null, ThemeKind theme = ThemeKind.Light) =>
        HtmlExporter.BuildAsync(content, "notes.md", theme, "body { color: red; }", embed ?? (_ => Task.FromResult<string?>(null)));

    [Fact]
    public async Task BuildsAPageThatLoadsNothing()
    {
        var html = await Build("<h1>Notes</h1>");

        Assert.StartsWith("<!DOCTYPE html>\n<html data-theme=\"light\"><head>", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.Contains($"<meta http-equiv=\"Content-Security-Policy\" content=\"{HtmlExporter.ContentSecurityPolicy}\">", html);
        Assert.Contains("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">", html);
        Assert.Contains("<title>notes.md</title>", html);
        Assert.Contains("<style>body { color: red; }</style>", html);
        Assert.Contains("<main class=\"markdown-body\"><h1>Notes</h1></main>", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public async Task MarksTheTheme()
    {
        Assert.Contains("<html data-theme=\"dark\">", await Build("", theme: ThemeKind.Dark));
    }

    [Fact]
    public async Task EscapesTheTitle()
    {
        var html = await HtmlExporter.BuildAsync("", "a</title><b>.md", ThemeKind.Light, "", _ => Task.FromResult<string?>(null));
        Assert.Contains("<title>a&lt;/title&gt;&lt;b&gt;.md</title>", html);
    }

    [Fact]
    public async Task EmbedsEachImageOnce()
    {
        var asked = new List<Uri>();
        var html = await Build(
            """<p><img src="https://doc.mdglint.example/C/docs/a.png" alt="a"><img src="https://doc.mdglint.example/C/docs/a.png" alt="again"></p>""",
            uri =>
            {
                asked.Add(uri);
                return Task.FromResult<string?>(Png);
            });

        Assert.Equal([new Uri("https://doc.mdglint.example/C/docs/a.png")], asked);
        Assert.Contains($"<img src=\"{Png}\" alt=\"a\"><img src=\"{Png}\" alt=\"again\">", html);
    }

    [Fact]
    public async Task LeavesOutImagesThatCantBeEmbedded()
    {
        var html = await Build(
            """<img src="http://example.com/blocked.png" alt="blocked"><img src="https://example.com/page" alt="not an image">""",
            uri => Task.FromResult<string?>(uri.Host == "example.com" && uri.AbsolutePath == "/page" ? "data:text/html,hi" : null));

        Assert.Contains("<img alt=\"blocked\"><img alt=\"not an image\">", html);
    }

    [Theory]
    [InlineData("<img alt=\"none\">")]
    [InlineData("<img src=\"images/a.png\" alt=\"none\">")]
    [InlineData("<img src=\"file:///C:/a.png\" alt=\"none\">")]
    [InlineData("<img src=\"data:text/html,hi\" alt=\"none\">")]
    public async Task LeavesOutImagesWithoutAWebAddress(string image)
    {
        var html = await Build(image, _ => throw new InvalidOperationException("Not asked."));
        Assert.Contains("<img alt=\"none\">", html);
    }

    [Fact]
    public async Task KeepsEmbeddedImages()
    {
        var html = await Build($"<img src=\"{Png}\" srcset=\"https://example.com/2x.png 2x\" sizes=\"10px\">");
        Assert.Contains($"<img src=\"{Png}\">", html);
    }

    [Fact]
    public async Task LeavesOutVideoAudioAndPictureSources()
    {
        var html = await Build(
            """<video src="https://example.com/a.mp4"></video><audio controls=""><source src="a.mp3"></audio><picture><source srcset="dark.png"><img alt="x"></picture>""");

        Assert.Contains("<main class=\"markdown-body\"><picture><img alt=\"x\"></picture></main>", html);
    }
}
