using MDGlint.Core.Contracts;
using MDGlint.Core.Services;

namespace MDGlint.Core.Tests;

public sealed class MarkdownRendererTests
{
    private readonly MarkdownRenderer _renderer = new();

    [Fact]
    public void Render_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _renderer.Render(null!));
    }

    [Theory]
    [InlineData("# Title", "<h1 id=\"title\">Title</h1>")]
    [InlineData("- [x] done", "<input disabled=\"disabled\" type=\"checkbox\" checked=\"checked\"> done")]
    [InlineData("| a | b |\n|:-|-:|\n| 1 | 2 |", "<td style=\"text-align: right\">2</td>")]
    [InlineData("Text[^1]\n\n[^1]: Note.", "<a id=\"fnref:1\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a>")]
    [InlineData("Text[^1]\n\n[^1]: Note.", "<li id=\"fn:1\">")]
    [InlineData("```js\nvar x = 1;\n```", "<pre><code class=\"language-js\">var x = 1;\n</code></pre>")]
    [InlineData("Visit https://example.com today", "<a href=\"https://example.com\">https://example.com</a>")]
    [InlineData("![alt](img/a.png)", "<img src=\"img/a.png\" alt=\"alt\">")]
    // Absolute Windows paths: Markdig encodes the backslashes, and the preview decodes them.
    [InlineData(@"![alt](C:\pics\a.png)", "<img src=\"C:%5Cpics%5Ca.png\" alt=\"alt\">")]
    [InlineData(@"[notes](D:\docs\b.md)", "<a href=\"D:%5Cdocs%5Cb.md\">notes</a>")]
    [InlineData("[next](../next.md)", "<a href=\"../next.md\">next</a>")]
    [InlineData("<details>\n<summary>More</summary>\n\nHidden\n\n</details>", "<summary>More</summary>")]
    [InlineData("> [!WARNING]\n> Careful.", "<div class=\"markdown-alert markdown-alert-warning\">\n<p class=\"markdown-alert-title\">Warning</p>")]
    public void Render_MarkdownFeatures_SurviveSanitizing(string markdown, string expected)
    {
        Assert.Contains(expected, _renderer.Render(markdown));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "<script")]
    [InlineData("<img src=x onerror=alert(1)>", "onerror")]
    [InlineData("[click](javascript:alert(1))", "javascript:")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "javascript:")]
    [InlineData("<iframe src=\"https://example.com\"></iframe>", "<iframe")]
    [InlineData("<style>body{display:none}</style>", "<style")]
    [InlineData("Text <span onmouseover=\"alert(1)\">x</span>", "onmouseover")]
    public void Render_RawHtml_IsSanitized(string markdown, string forbidden)
    {
        Assert.DoesNotContain(forbidden, _renderer.Render(markdown), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_CodeSpansAndBlocks_ShowHtmlAsText()
    {
        var html = _renderer.Render("`<script>`\n\n```\n<script>alert(1)</script>\n```");

        Assert.Contains("<code>&lt;script&gt;</code>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
    }

    [Fact]
    public async Task RenderFileAsync_NullReader_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _renderer.RenderFileAsync("a.md", null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenderFileAsync_NoPath_ShowsOpenMessage(string? path)
    {
        var document = await _renderer.RenderFileAsync(path, new FakeReader(_ => throw new InvalidOperationException("not called")));

        Assert.Equal("<p class=\"message\">Open a Markdown file to preview it.</p>", document.Html);
        Assert.Null(document.BaseDirectory);
        Assert.True(document.OffersOpen);
    }

    [Fact]
    public async Task RenderFileAsync_ReadsAndSanitizes()
    {
        var path = Path.Combine(Path.GetTempPath(), "docs", "readme.md");
        string? requested = null;
        var reader = new FakeReader(p =>
        {
            requested = p;
            return "# Hi\n\n<script>alert(1)</script>";
        });

        var document = await _renderer.RenderFileAsync(path, reader);

        Assert.Equal(path, requested);
        Assert.Contains("<h1 id=\"hi\">Hi</h1>", document.Html);
        Assert.DoesNotContain("<script", document.Html);
        Assert.Equal(Path.GetDirectoryName(path), document.BaseDirectory);
        Assert.False(document.OffersOpen);
    }

    [Fact]
    public async Task RenderFileAsync_RelativePath_ResolvesBaseDirectory()
    {
        var document = await _renderer.RenderFileAsync("readme.md", new FakeReader(_ => "x"));

        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath("readme.md")), document.BaseDirectory);
    }

    public static TheoryData<Exception, string> ReadFailures => new()
    {
        { new FileNotFoundException(), "File not found." },
        { new DirectoryNotFoundException(), "File not found." },
        { new IOException(), "Invalid path." },
        { new UnauthorizedAccessException(), "Invalid path." },
        { new ArgumentException(), "Invalid path." },
        { new NotSupportedException(), "Invalid path." },
        { new InvalidOperationException(), "Unable to render this Markdown file." },
    };

    [Theory]
    [MemberData(nameof(ReadFailures))]
    public async Task RenderFileAsync_ReadFailure_ShowsMessage(Exception failure, string message)
    {
        var document = await _renderer.RenderFileAsync("a.md", new FakeReader(_ => throw failure));

        Assert.Equal($"<p class=\"message\">{message}</p>", document.Html);
        Assert.Null(document.BaseDirectory);
        Assert.False(document.OffersOpen);
    }

    [Fact]
    public async Task RenderFileAsync_Cancelled_Throws()
    {
        var reader = new FakeReader(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => _renderer.RenderFileAsync("a.md", reader));
    }

    [Fact]
    public async Task RenderFileAsync_PassesCancellationToken()
    {
        using var source = new CancellationTokenSource();
        CancellationToken received = default;
        var reader = new FakeReader((_, token) =>
        {
            received = token;
            return "x";
        });

        await _renderer.RenderFileAsync("a.md", reader, source.Token);

        Assert.Equal(source.Token, received);
    }

    private sealed class FakeReader(Func<string, CancellationToken, string> read) : IMarkdownFileReader
    {
        public FakeReader(Func<string, string> read)
            : this((path, _) => read(path))
        {
        }

        public Task<string> ReadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(read(filePath, cancellationToken));
        }
    }
}
