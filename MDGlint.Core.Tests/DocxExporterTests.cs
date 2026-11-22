using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using MDGlint.Core.Models;
using MDGlint.Core.Services;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace MDGlint.Core.Tests;

public sealed class DocxExporterTests : IDisposable
{
    private const string LocalImage = "https://doc.mdglint.example/C/docs/a.png";

    private readonly List<WordprocessingDocument> _opened = [];

    public void Dispose()
    {
        foreach (var document in _opened)
        {
            document.Dispose();
        }
    }

    /// <summary>Exports the HTML and opens the result, after checking Word's schema accepts it.</summary>
    private async Task<WordprocessingDocument> Export(string html, Func<Uri, Task<ExportImage?>>? loadImage = null)
    {
        var bytes = await DocxExporter.BuildAsync(html, "notes.md", loadImage ?? (_ => Task.FromResult<ExportImage?>(null)));
        var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        _opened.Add(document);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(document)
            .Select(error => $"{error.Path?.XPath}: {error.Description}").ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        return document;
    }

    private async Task<Body> ExportBody(string html, Func<Uri, Task<ExportImage?>>? loadImage = null) =>
        (await Export(html, loadImage)).MainDocumentPart!.Document!.Body!;

    private static Func<Uri, Task<ExportImage?>> Serve(byte[] bytes, string type = "image/png", List<Uri>? asked = null) => uri =>
    {
        asked?.Add(uri);
        return Task.FromResult<ExportImage?>(new ExportImage(type, bytes));
    };

    private static string? Style(Paragraph paragraph) => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

    private static Paragraph[] Paragraphs(OpenXmlElement element) => element.Elements<Paragraph>().ToArray();

    private static Run RunWith(OpenXmlElement element, string text) =>
        element.Descendants<Run>().Single(run => run.InnerText == text);

    [Fact]
    public async Task BuildsADocumentInWordsOwnStyles()
    {
        var document = await Export("<h1>Notes</h1><p>Text</p>");

        Assert.Equal("notes.md", document.PackageProperties.Title);
        var paragraphs = Paragraphs(document.MainDocumentPart!.Document!.Body!);
        Assert.Equal(["Notes", "Text"], paragraphs.Select(p => p.InnerText));
        Assert.Equal(new string?[] { "Heading1", null }, paragraphs.Select(Style).ToArray());

        var styles = document.MainDocumentPart.StyleDefinitionsPart!.Styles!.Elements<Style>().Select(s => s.StyleId!.Value!).ToArray();
        Assert.Equal(
            ["Normal", "Heading1", "Heading2", "Heading3", "Heading4", "Heading5", "Heading6", "Quote", "CodeBlock",
             "FootnoteText", "InlineCode", "Hyperlink", "FootnoteReference"],
            styles);
        Assert.NotNull(document.MainDocumentPart.NumberingDefinitionsPart);
        Assert.Equal("15", document.MainDocumentPart.DocumentSettingsPart!.Settings!.Descendants<CompatibilitySetting>().Single().Val!.Value);
        Assert.Null(document.MainDocumentPart.FootnotesPart);
        Assert.Empty(document.MainDocumentPart.ImageParts);
        Assert.Empty(document.MainDocumentPart.HyperlinkRelationships);
    }

    [Fact]
    public async Task UsesTheHeadingStyleForEachLevel()
    {
        var body = await ExportBody("<h1>1</h1><h2>2</h2><h3>3</h3><h4>4</h4><h5>5</h5><h6>6</h6>");
        Assert.Equal(["Heading1", "Heading2", "Heading3", "Heading4", "Heading5", "Heading6"], Paragraphs(body).Select(Style));
    }

    [Fact]
    public async Task FormatsInlineText()
    {
        var body = await ExportBody(
            "<p><strong>b1</strong><b>b2</b><em>i1</em><i>i2</i><cite>i3</cite><dfn>i4</dfn><var>i5</var>"
            + "<del>s1</del><s>s2</s><strike>s3</strike><ins>u1</ins><u>u2</u>"
            + "<code>c1</code><kbd>c2</kbd><samp>c3</samp><tt>c4</tt><sup>up</sup><sub>down</sub><mark>m</mark><span>plain</span><!-- note --></p>");

        Assert.All(["b1", "b2"], text => Assert.NotNull(RunWith(body, text).RunProperties!.Bold));
        Assert.All(["i1", "i2", "i3", "i4", "i5"], text => Assert.NotNull(RunWith(body, text).RunProperties!.Italic));
        Assert.All(["s1", "s2", "s3"], text => Assert.NotNull(RunWith(body, text).RunProperties!.Strike));
        Assert.All(["u1", "u2"], text => Assert.Equal(UnderlineValues.Single, RunWith(body, text).RunProperties!.Underline!.Val!.Value));
        Assert.All(["c1", "c2", "c3", "c4"], text => Assert.Equal("InlineCode", RunWith(body, text).RunProperties!.RunStyle!.Val!.Value));
        Assert.Equal(VerticalPositionValues.Superscript, RunWith(body, "up").RunProperties!.VerticalTextAlignment!.Val!.Value);
        Assert.Equal(VerticalPositionValues.Subscript, RunWith(body, "down").RunProperties!.VerticalTextAlignment!.Val!.Value);
        Assert.Equal(HighlightColorValues.Yellow, RunWith(body, "m").RunProperties!.Highlight!.Val!.Value);
        Assert.Null(RunWith(body, "plain").RunProperties);
        Assert.Equal("b1b2i1i2i3i4i5s1s2s3u1u2c1c2c3c4updownmplain", body.InnerText);
    }

    [Fact]
    public async Task CollapsesWhiteSpaceAsHtmlDoes()
    {
        var body = await ExportBody("\n<p>  one \n\t two  </p>\n<p>line<br>  next <img alt=\"\"></p>");

        var paragraphs = Paragraphs(body);
        Assert.Equal(["one two", "linenext"], paragraphs.Select(p => p.InnerText));
        Assert.Single(paragraphs[1].Descendants<Break>());
    }

    [Fact]
    public async Task KeepsOnlyWebAndMailLinks()
    {
        var document = await Export(
            "<p><a href=\"https://example.com/\">web <b>bold</b></a> <a href=\"mailto:a@example.com\">mail</a> "
            + "<a href=\"other.md\">file</a> <a href=\"#notes\">here</a> <a href=\"file:///C:/x.exe\">exe</a> <a>none</a> "
            + "<a href=\"http://example.com/code\"><code>code</code></a></p>");
        var main = document.MainDocumentPart!;
        var body = main.Document!.Body!;

        var links = body.Descendants<Hyperlink>().ToArray();
        Assert.Equal(["web bold", "mail", "code"], links.Select(link => link.InnerText));
        Assert.Equal(
            ["https://example.com/", "mailto:a@example.com", "http://example.com/code"],
            links.Select(link => main.HyperlinkRelationships.Single(r => r.Id == link.Id).Uri.OriginalString));
        Assert.All(main.HyperlinkRelationships, r => Assert.True(r.IsExternal));
        Assert.Equal("Hyperlink", RunWith(body, "bold").RunProperties!.RunStyle!.Val!.Value);
        var code = RunWith(body, "code").RunProperties!;
        Assert.Equal("InlineCode", code.RunStyle!.Val!.Value);
        Assert.Equal("0969DA", code.Color!.Val!.Value);
        Assert.NotNull(code.Underline);
        Assert.Null(RunWith(body, "file").RunProperties);
        Assert.Contains("file here exe none", body.InnerText);
    }

    [Fact]
    public async Task EndsALinkWhereABlockStarts()
    {
        // The HTML parser keeps a list inside a link (a <div> would end it).
        var body = await ExportBody("<a href=\"https://example.com/\">link<ul><li>item</li></ul>after</a>");

        Assert.Equal(["link", "item", "after"], Paragraphs(body).Select(p => p.InnerText));
        Assert.Equal("link", body.Descendants<Hyperlink>().Single().InnerText);
    }

    [Fact]
    public async Task NumbersAndNestsLists()
    {
        var document = await Export(
            "<ul><li>one<ol start=\"3\"><li>three</li><li><p>four</p><p>more</p></li></ol></li><li>two</li></ul>"
            + "<ol><li>again</li></ol><ol start=\"x\"><li>bad start</li></ol>");
        var body = document.MainDocumentPart!.Document!.Body!;
        var paragraphs = Paragraphs(body);
        Assert.Equal(["one", "three", "four", "more", "two", "again", "bad start"], paragraphs.Select(p => p.InnerText));

        static (int?, int?, string?, string?) Numbered(Paragraph p) => (
            p.ParagraphProperties!.NumberingProperties?.NumberingId?.Val?.Value,
            p.ParagraphProperties.NumberingProperties?.NumberingLevelReference?.Val?.Value,
            p.ParagraphProperties.Indentation?.Left?.Value,
            p.ParagraphProperties.Indentation?.Hanging?.Value);

        Assert.Equal(
            [(1, 0, "720", "360"), (3, 1, "1440", "360"), (3, 1, "1440", "360"), (null, null, "1440", null),
             (1, 0, "720", "360"), (4, 0, "720", "360"), (5, 0, "720", "360")],
            paragraphs.Select(Numbered));

        // One bulleted list for all, and each numbered list restarting from its own start.
        var instances = document.MainDocumentPart.NumberingDefinitionsPart!.Numbering!.Elements<NumberingInstance>().ToArray();
        Assert.Equal([1, 3, 4, 5], instances.Select(n => n.NumberID!.Value));
        Assert.Equal([1, 2, 2, 2], instances.Select(n => n.AbstractNumId!.Val!.Value));
        Assert.Equal([3, 1, 1], instances.Skip(1)
            .Select(n => n.GetFirstChild<LevelOverride>()!.StartOverrideNumberingValue!.Val!.Value));
        Assert.Equal(1, instances[1].GetFirstChild<LevelOverride>()!.LevelIndex!.Value);
    }

    [Fact]
    public async Task ShowsTaskListsWithCheckboxesInsteadOfBullets()
    {
        var body = await ExportBody(
            "<ul class=\"contains-task-list\"><li class=\"task-list-item\"><input disabled type=\"checkbox\"> todo</li>"
            + "<li class=\"task-list-item\"><input disabled type=\"checkbox\" checked> done</li><li>not a task</li></ul>");

        var paragraphs = Paragraphs(body);
        Assert.Equal(["☐ todo", "☑ done", "not a task"], paragraphs.Select(p => p.InnerText));
        Assert.Equal([false, false, true], paragraphs.Select(p => p.ParagraphProperties!.NumberingProperties is not null));
        Assert.All(paragraphs, p => Assert.Equal("720", p.ParagraphProperties!.Indentation!.Left!.Value));
    }

    [Fact]
    public async Task KeepsTextThatSitsDirectlyInAList()
    {
        var body = await ExportBody("<ul>loose<li>item</li></ul>");
        Assert.Equal(["loose", "item"], Paragraphs(body).Select(p => p.InnerText));
    }

    [Fact]
    public async Task StopsNestingListsAtWordsDeepestLevel()
    {
        var html = string.Concat(Enumerable.Repeat("<ul><li>x", 11)) + string.Concat(Enumerable.Repeat("</li></ul>", 11));
        var body = await ExportBody(html);

        var levels = Paragraphs(body).Select(p => p.ParagraphProperties!.NumberingProperties!.NumberingLevelReference!.Val!.Value);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 8, 8], levels);
    }

    [Fact]
    public async Task WritesCodeBlocksLineByLine()
    {
        var body = await ExportBody(
            "<pre><code class=\"language-js\"><span class=\"hljs-keyword\">const</span> x\t= 1;\r\n\n  end\n</code></pre>"
            + "<pre>no newline</pre>");

        var paragraphs = Paragraphs(body);
        Assert.Equal(["const x= 1;", "", "  end", "no newline"], paragraphs.Select(p => p.InnerText));
        Assert.All(paragraphs, p => Assert.Equal("CodeBlock", Style(p)));
        Assert.Single(paragraphs[0].Descendants<TabChar>());
        Assert.Equal([null, null, "160", "160"], paragraphs.Select(p => p.ParagraphProperties!.SpacingBetweenLines?.After?.Value));
    }

    [Fact]
    public async Task IndentsAndMarksQuotesAndAlerts()
    {
        var body = await ExportBody(
            "<blockquote><p>outer</p><blockquote><p>inner</p></blockquote><h2>heading</h2></blockquote>"
            + "<div class=\"markdown-alert markdown-alert-note\"><p class=\"markdown-alert-title\">Note</p><p>Body</p></div>");

        var paragraphs = Paragraphs(body);
        Assert.Equal(["outer", "inner", "heading", "Note", "Body"], paragraphs.Select(p => p.InnerText));
        Assert.Equal(["Quote", "Quote", "Heading2", "Quote", "Quote"], paragraphs.Select(Style));
        Assert.Equal(["360", "720", "360", "360", "360"], paragraphs.Select(p => p.ParagraphProperties!.Indentation!.Left!.Value));
        Assert.Equal(
            ["D0D7DE", "D0D7DE", "D0D7DE", "0969DA", "0969DA"], // An alert's border in GitHub's colour for it.
            paragraphs.Select(p => p.ParagraphProperties!.ParagraphBorders!.LeftBorder!.Color!.Value));
        Assert.NotNull(RunWith(body, "Note").RunProperties!.Bold);
    }

    [Fact]
    public async Task DrawsARuleAndSetsOutDefinitionsAndDetails()
    {
        var body = await ExportBody(
            "<hr><dl><dt>Term</dt><dd>Meaning</dd></dl><details><summary>More</summary><p>Hidden</p></details>"
            + "<section>Loose</section><div>Plain</div>");

        var paragraphs = Paragraphs(body);
        Assert.Equal(["", "Term", "Meaning", "More", "Hidden", "Loose", "Plain"], paragraphs.Select(p => p.InnerText));
        Assert.Null(paragraphs[6].ParagraphProperties!.ParagraphBorders);
        Assert.NotNull(paragraphs[0].ParagraphProperties!.ParagraphBorders!.BottomBorder);
        Assert.NotNull(RunWith(body, "Term").RunProperties!.Bold);
        Assert.Equal("720", paragraphs[2].ParagraphProperties!.Indentation!.Left!.Value);
        Assert.NotNull(RunWith(body, "More").RunProperties!.Bold);
    }

    [Fact]
    public async Task BuildsTablesWithAHeaderRowAndAlignment()
    {
        var body = await ExportBody(
            "<table><caption>Caption</caption><colgroup><col></colgroup>"
            + "<thead><tr><th style=\"text-align: left\">a</th><th style=\"text-align: center\">b</th><th style=\"text-align: right\">c</th></tr></thead>"
            + "<tbody><tr><td align=\"RIGHT\">1</td><td colspan=\"2\">wide</td></tr><tr><td colspan=\"1\">short</td></tr><tr></tr>"
            + "<tr><td colspan=\"x\"></td><th>side</th><td><pre>code</pre></td></tr></tbody></table>");

        Assert.Equal("Caption", Paragraphs(body).Single().InnerText);
        Assert.NotNull(RunWith(body, "Caption").RunProperties!.Italic);
        var table = body.Elements<Table>().Single();
        Assert.Equal(3, table.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Count());
        Assert.Null(table.GetFirstChild<TableProperties>()!.TableIndentation);

        var rows = table.Elements<TableRow>().ToArray();
        Assert.Equal(5, rows.Length);
        Assert.NotNull(rows[0].TableRowProperties!.GetFirstChild<TableHeader>());
        Assert.Null(rows[1].TableRowProperties);

        var header = rows[0].Elements<TableCell>().ToArray();
        Assert.All(header, cell => Assert.Equal("F6F8FA", cell.TableCellProperties!.Shading!.Fill!.Value));
        Assert.NotNull(RunWith(header[0], "a").RunProperties!.Bold);
        Assert.Equal(
            [null, JustificationValues.Center, JustificationValues.Right],
            header.Select(cell => cell.GetFirstChild<Paragraph>()!.ParagraphProperties!.Justification?.Val?.Value));
        Assert.All(header, cell => Assert.Equal("0", cell.GetFirstChild<Paragraph>()!.ParagraphProperties!.SpacingBetweenLines!.After!.Value));

        var body1 = rows[1].Elements<TableCell>().ToArray();
        Assert.Equal(JustificationValues.Right, body1[0].GetFirstChild<Paragraph>()!.ParagraphProperties!.Justification!.Val!.Value);
        Assert.Null(body1[0].TableCellProperties!.Shading);
        Assert.Equal(2, body1[1].TableCellProperties!.GridSpan!.Val!.Value);

        // Short and empty rows are filled to the grid.
        Assert.Equal([1, 2], rows[2].Elements<TableCell>().Select(cell => cell.TableCellProperties!.GridSpan?.Val?.Value ?? 1));
        Assert.Equal(3, rows[3].Elements<TableCell>().Single().TableCellProperties!.GridSpan!.Val!.Value);

        var last = rows[4].Elements<TableCell>().ToArray();
        Assert.Equal("", last[0].InnerText); // An empty cell still holds a paragraph.
        Assert.NotNull(last[1].TableCellProperties!.Shading);
        Assert.Equal("0", last[2].GetFirstChild<Paragraph>()!.ParagraphProperties!.SpacingBetweenLines!.After!.Value);
    }

    [Fact]
    public async Task NestsTablesAndIndentsThemInLists()
    {
        var body = await ExportBody("<ul><li>item<table><tr><td><table><tr><td>inner</td></tr></table></td></tr></table></li></ul>");

        var outer = body.Elements<Table>().Single();
        Assert.Equal(720, outer.GetFirstChild<TableProperties>()!.TableIndentation!.Width!.Value);
        var cell = outer.Descendants<TableCell>().First();
        Assert.IsType<Table>(cell.ChildElements[1]);
        Assert.IsType<Paragraph>(cell.LastChild); // Word needs a cell to end with one.
    }

    [Fact]
    public async Task LeavesOutEmptyTables()
    {
        var body = await ExportBody("<table><tbody><tr></tr></tbody></table><p>after</p>");
        Assert.Empty(body.Elements<Table>());
    }

    [Fact]
    public async Task StoresEachImageOnceAtItsSize()
    {
        var asked = new List<Uri>();
        var document = await Export(
            $"<p><img src=\"{LocalImage}\" alt=\"first\"><img src=\"{LocalImage}\" alt=\"again\"></p>",
            Serve(ImageInfoTests.Png(200, 100), "image/webp", asked));
        var main = document.MainDocumentPart!;

        Assert.Equal([new Uri(LocalImage)], asked);
        var part = Assert.Single(main.ImageParts);
        Assert.Equal("image/png", part.ContentType); // From the header, not the type it was served as.
        var drawings = main.Document!.Body!.Descendants<Drawing>().ToArray();
        Assert.Equal(2, drawings.Length);
        Assert.All(drawings, drawing =>
        {
            Assert.Equal(200 * 9525, drawing.Inline!.Extent!.Cx!.Value);
            Assert.Equal(100 * 9525, drawing.Inline.Extent.Cy!.Value);
        });
        Assert.Equal(["first", "again"], drawings.Select(d => d.Inline!.GetFirstChild<DW.DocProperties>()!.Description!.Value));
        Assert.Empty(main.ExternalRelationships); // Stored, never linked.
    }

    [Theory]
    [InlineData("", 1000, 500, 576, 288)] // Too wide for the page.
    [InlineData("width=\"100\"", 200, 100, 100, 50)]
    [InlineData("height=\"25px\"", 200, 100, 50, 25)]
    [InlineData("width=\"30\" height=\"40\"", 200, 100, 30, 40)]
    [InlineData("width=\"50%\" height=\"0\"", 200, 100, 200, 100)]
    [InlineData("width=\"70000\"", 200, 100, 200, 100)]
    [InlineData("width=\"1\"", 2000, 1, 1, 1)]
    public async Task SizesImagesAsTheHtmlSays(string attributes, uint width, uint height, long shownWidth, long shownHeight)
    {
        var body = await ExportBody($"<img src=\"{LocalImage}\" {attributes}>", Serve(ImageInfoTests.Png(width, height)));

        var extent = body.Descendants<DW.Extent>().Single();
        Assert.Equal((shownWidth * 9525, shownHeight * 9525), (extent.Cx!.Value, extent.Cy!.Value));
    }

    [Fact]
    public async Task ReadsEmbeddedDataImages()
    {
        var data = Convert.ToBase64String(ImageInfoTests.Png(4, 4));
        var document = await Export($"<img src=\" data:image/png;base64,{data}\">", _ => throw new InvalidOperationException("Not asked."));

        Assert.Single(document.MainDocumentPart!.ImageParts);
    }

    public static TheoryData<string, string> ImagesLeftOut => new()
    {
        { "<img alt=\"no source\">", "no source" },
        { "<img src=\"images/a.png\" alt=\"relative\">", "relative" },
        { "<img src=\"file:///C:/a.png\" alt=\"file\">", "file" },
        { "<img src=\"data:image/png;base64,QUJ\" alt=\"bad data\">", "bad data" },
        { "<img src=\"data:image/png;base64,!!!\" alt=\"not base64\">", "not base64" },
        { "<img src=\"data:image/png;base64,QUJD\" alt=\"not an image\">", "not an image" },
        { "<img src=\"https://example.com/missing.png\" alt=\"missing\">", "missing" },
        { "<img src=\"https://example.com/page\" alt=\"html\">", "html" },
        { "<img src=\"https://example.com/text.png\" alt=\"text\">", "text" },
    };

    [Theory]
    [MemberData(nameof(ImagesLeftOut))]
    public async Task ShowsTheAltTextForImagesItCantStore(string image, string alt)
    {
        var body = await ExportBody(image, uri => Task.FromResult<ExportImage?>(uri.AbsolutePath switch
        {
            "/page" => new ExportImage("text/html", ImageInfoTests.Png(1, 1)),
            "/text.png" => new ExportImage("image/png", "not really"u8.ToArray()),
            "/missing.png" => null,
            _ => throw new InvalidOperationException("Not asked."),
        }));

        Assert.Empty(body.Descendants<Drawing>());
        Assert.Equal(alt, body.InnerText);
        Assert.NotNull(RunWith(body, alt).RunProperties!.Italic);
    }

    [Fact]
    public async Task LeavesOutVideoAndAudio()
    {
        var body = await ExportBody(
            "<p>a<video src=\"https://example.com/a.mp4\">fallback</video><audio><source src=\"a.mp3\"></audio>b"
            + "<picture><source srcset=\"dark.png\"><img alt=\"c\"></picture></p>");
        Assert.Equal("abc", body.InnerText);
    }

    [Fact]
    public async Task TurnsFootnotesIntoWordFootnotes()
    {
        var document = await Export(
            $"<p>Text<a id=\"fnref:1\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a> again<a href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a>"
            + "<a href=\"#fn:2\" class=\"footnote-ref\"><sup>2</sup></a><a href=\"#fn:9\" class=\"footnote-ref\"><sup>9</sup></a><a class=\"footnote-ref\">?</a>.</p>"
            + "<div class=\"footnotes\"><hr><ol><li id=\"fn:1\"><p>The <a href=\"https://example.com/\">note</a>."
            + $"<img src=\"{LocalImage}\"><a href=\"#fnref:1\" class=\"footnote-back-ref\">↩</a></p></li><li id=\"fn:2\"></li></ol>loose</div>",
            Serve(ImageInfoTests.Png(2, 2)));
        var main = document.MainDocumentPart!;
        var body = main.Document!.Body!;

        // The second reference to a note, and references to notes that aren't there, stay as text.
        Assert.Equal(["Text again19?."], Paragraphs(body).Select(p => p.InnerText));
        Assert.Equal([1L, 2L], body.Descendants<FootnoteReference>().Select(r => r.Id!.Value));

        var part = main.FootnotesPart!;
        var notes = part.Footnotes!.Elements<Footnote>().ToArray();
        Assert.Equal([-1L, 0L, 1L, 2L], notes.Select(n => n.Id!.Value));
        Assert.Equal(" The note.", notes[2].InnerText);
        Assert.Equal("FootnoteText", Style(notes[2].Elements<Paragraph>().Single()));
        Assert.NotNull(notes[2].Descendants<FootnoteReferenceMark>().SingleOrDefault());
        Assert.NotNull(notes[3].Descendants<FootnoteReferenceMark>().SingleOrDefault());

        // Its link and image belong to the footnotes, not the body.
        Assert.Single(part.HyperlinkRelationships);
        Assert.Single(part.ImageParts);
        Assert.Empty(main.HyperlinkRelationships);
        Assert.Empty(main.ImageParts);
        Assert.Equal(2, main.DocumentSettingsPart!.Settings!.Descendants<FootnoteSpecialReference>().Count());
    }
}
