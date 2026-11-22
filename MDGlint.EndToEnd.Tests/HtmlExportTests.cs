using System.Text.RegularExpressions;

namespace MDGlint.EndToEnd.Tests;

/// <summary>Export ▸ HTML: one self-contained page, with images under the preview's rules.</summary>
public sealed class HtmlExportTests : IDisposable
{
    private readonly string _docs = AppSession.NewFolder();

    public void Dispose() => Directory.Delete(_docs, recursive: true);

    [EndToEndFact]
    public async Task ExportsASelfContainedPage()
    {
        using var server = new LocalServer(path => path.StartsWith("/remote.png", StringComparison.Ordinal)
            ? new LocalServer.Response(200, "image/png", PreviewTests.Png)
            : LocalServer.NotFound);
        File.WriteAllBytes(Path.Combine(_docs, "local.png"), PreviewTests.Png);
        var file = Path.Combine(_docs, "notes.md");
        File.WriteAllText(file,
            $"# Notes\n\n![local](local.png)\n\n![remote]({server.Origin}/remote.png)\n\n```js\nconst x = 1;\n```\n");
        var html = Path.Combine(_docs, "notes.html");

        await using var app = await AppSession.StartAsync(file, new AppOptions
        {
            Settings = """{"CheckForUpdates":false,"Theme":"dark"}""",
        });
        await app.Page.WaitForAsync("document.querySelector('.copy-code') !== null");

        // Remote content is blocked for this file, so the remote image is left out.
        var suggested = await app.ExportWithSuggestedNameAsync("HTML…", "Export to HTML");
        Assert.True(suggested is "notes.html" or "notes", $"The dialog suggested \"{suggested}\".");
        var page = await ReadExportAsync(html);

        Assert.StartsWith("<!DOCTYPE html>\n<html data-theme=\"dark\">", page);
        Assert.Contains("<title>notes.md</title>", page);
        Assert.Contains("Content-Security-Policy", page);
        Assert.Contains("github-markdown-css", page); // The styles, inlined.
        Assert.Contains("<h1 id=\"notes\">Notes</h1>", page);
        Assert.Contains("class=\"hljs-keyword\"", page); // Highlighted as in the preview.
        Assert.DoesNotContain("copy-code", page); // Without the preview's own controls.
        Assert.DoesNotContain("mdglint.example", page);
        Assert.DoesNotContain(server.Origin, page);
        Assert.Contains("<img src=\"data:image/png;base64,", page);
        Assert.Contains("<img alt=\"remote\">", page);
        Assert.Empty(server.Requests);

        // Allowed for the file: now the remote image is embedded too.
        await app.InvokeAsync("LoadRemoteButton");
        await app.Page.WaitForAsync("[...document.images].every(image => image.complete && image.naturalWidth === 1)");
        File.Delete(html);
        await app.ExportWithSuggestedNameAsync("HTML…", "Export to HTML");
        page = await ReadExportAsync(html);

        Assert.Equal(2, Regex.Matches(page, "<img src=\"data:image/png;base64,").Count);
        Assert.DoesNotContain(server.Origin, page);
    }

    private static async Task<string> ReadExportAsync(string path)
    {
        string? text = null;
        await AppSession.WaitUntilAsync(() =>
        {
            try
            {
                text = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (IOException)
            {
                text = null; // Still being written.
            }

            return text?.TrimEnd().EndsWith("</html>", StringComparison.Ordinal) == true;
        }, "The HTML wasn't saved.", seconds: 30);
        return text!;
    }
}
