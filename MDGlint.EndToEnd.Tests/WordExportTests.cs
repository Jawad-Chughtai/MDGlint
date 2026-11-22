using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MDGlint.EndToEnd.Tests;

/// <summary>Export ▸ Word: a .docx in Word's styles, with images stored under the preview's rules.</summary>
public sealed class WordExportTests : IDisposable
{
    private readonly string _docs = AppSession.NewFolder();

    public void Dispose() => Directory.Delete(_docs, recursive: true);

    [EndToEndFact]
    public async Task ExportsAWordDocument()
    {
        using var server = new LocalServer(path => path.StartsWith("/remote.png", StringComparison.Ordinal)
            ? new LocalServer.Response(200, "image/png", PreviewTests.Png)
            : LocalServer.NotFound);
        File.WriteAllBytes(Path.Combine(_docs, "local.png"), PreviewTests.Png);
        var file = Path.Combine(_docs, "notes.md");
        File.WriteAllText(file,
            $"# Notes\n\nSee [the site](https://example.com/) and a footnote[^1].\n\n![local](local.png)\n\n"
            + $"![remote]({server.Origin}/remote.png)\n\n- one\n- two\n\n| a | b |\n|---|--:|\n| 1 | 2 |\n\n"
            + "```js\nconst x = 1;\n```\n\n<script>alert(1)</script>\n\n[^1]: The note.\n");
        var docx = Path.Combine(_docs, "notes.docx");

        await using var app = await AppSession.StartAsync(file, new AppOptions
        {
            Settings = """{"CheckForUpdates":false,"Theme":"dark"}""",
        });
        await app.Page.WaitForAsync("document.querySelector('.copy-code') !== null");

        // Remote content is blocked for this file, so the remote image is left out.
        var suggested = await app.ExportWithSuggestedNameAsync("Word…", "Export to Word");
        Assert.True(suggested is "notes.docx" or "notes", $"The dialog suggested \"{suggested}\".");
        using (var document = await OpenExportAsync(docx))
        {
            var main = document.MainDocumentPart!;
            var body = main.Document!.Body!;
            Assert.Empty(new OpenXmlValidator(FileFormatVersions.Office2019).Validate(document));
            Assert.Equal("notes.md", document.PackageProperties.Title);

            var heading = body.Elements<Paragraph>().First();
            Assert.Equal(("Notes", "Heading1"), (heading.InnerText, heading.ParagraphProperties!.ParagraphStyleId!.Val!.Value));
            Assert.Equal("https://example.com/", main.HyperlinkRelationships.Single().Uri.OriginalString);
            Assert.Equal("The note.", main.FootnotesPart!.Footnotes!.Elements<Footnote>().Last().InnerText.Trim());
            Assert.Equal(2, body.Descendants<NumberingProperties>().Count());
            Assert.Single(body.Elements<Table>());
            Assert.Contains(body.Elements<Paragraph>(), p => p.ParagraphProperties?.ParagraphStyleId?.Val == "CodeBlock" && p.InnerText == "const x = 1;");
            Assert.DoesNotContain("alert", body.InnerText); // Sanitized, as in the preview.

            Assert.Single(main.ImageParts);
            Assert.Contains("remote", body.InnerText); // Its alt text, in its place.
            Assert.DoesNotContain(main.ExternalRelationships, r => r.Uri.ToString().Contains(server.Origin));
            Assert.Empty(server.Requests);
        }

        // Allowed for the file: now the remote image is stored too.
        await app.InvokeAsync("LoadRemoteButton");
        await app.Page.WaitForAsync("[...document.images].every(image => image.complete && image.naturalWidth === 1)");
        File.Delete(docx);
        await app.ExportWithSuggestedNameAsync("Word…", "Export to Word");
        using (var document = await OpenExportAsync(docx))
        {
            Assert.Equal(2, document.MainDocumentPart!.ImageParts.Count());
            Assert.DoesNotContain("remote", document.MainDocumentPart.Document!.Body!.InnerText);
        }
    }

    private static async Task<WordprocessingDocument> OpenExportAsync(string path)
    {
        WordprocessingDocument? document = null;
        await AppSession.WaitUntilAsync(() =>
        {
            try
            {
                document = File.Exists(path) ? WordprocessingDocument.Open(path, isEditable: false) : null;
            }
            catch (Exception ex) when (ex is IOException or OpenXmlPackageException or InvalidDataException)
            {
                document = null; // Still being written.
            }

            return document is not null;
        }, "The Word document wasn't saved.", seconds: 30);
        return document!;
    }
}
