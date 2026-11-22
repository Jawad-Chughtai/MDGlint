using System.Windows.Automation;

namespace MDGlint.EndToEnd.Tests;

/// <summary>Printing and PDF export: the theme a printout uses, and the saved PDF.</summary>
public sealed class PrintTests : IDisposable
{
    private const string Light = "rgb(255, 255, 255)";
    private const string Dark = "rgb(13, 17, 23)";

    private readonly string _docs = AppSession.NewFolder();

    public void Dispose() => Directory.Delete(_docs, recursive: true);

    [EndToEndFact]
    public async Task PrintsInTheLightThemeUntilTurnedOff()
    {
        await using var app = await AppSession.StartAsync(Write("notes.md", "# Notes\n\n```js\nlet x = 1;\n```"), new AppOptions
        {
            Settings = """{"CheckForUpdates":false,"Theme":"dark"}""",
        });
        var page = app.Page;
        await page.WaitForAsync("document.querySelector('h1')?.textContent === 'Notes'");

        // On screen the file stays dark; printed, the Markdown and the page are light.
        Assert.Equal(Dark, await Background(page, ".markdown-body"));
        await page.SendAsync("Emulation.setEmulatedMedia", new { media = "print" });
        Assert.Equal(Light, await Background(page, ".markdown-body"));
        Assert.Equal(Light, await Background(page, "body"));

        var item = await app.MenuItemAsync("Print in light theme");
        Assert.Equal(ToggleState.On, ((TogglePattern)item.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);
        ((InvokePattern)item.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

        await page.WaitForAsync($"getComputedStyle(document.querySelector('.markdown-body')).backgroundColor === '{Dark}'",
            "Printing didn't switch to the dark theme.");
        Assert.Equal(Dark, await Background(page, "body"));
        await AppSession.WaitUntilAsync(() => !WindowTests.Settings(app).GetProperty("PrintInLightTheme").GetBoolean(),
            "The choice wasn't saved.");
    }

    [EndToEndFact]
    public async Task ExportsThePdfBesideTheFile()
    {
        var file = Write("notes.md", "# Notes\n\nSome text.");
        // The suggested name, in the file's folder: the test keeps the dialog's suggestion.
        var pdf = Path.Combine(_docs, "notes.pdf");

        await using var app = await AppSession.StartAsync(file);
        await app.Page.WaitForAsync("document.querySelector('h1')?.textContent === 'Notes'");

        var suggested = await app.ExportWithSuggestedNameAsync("PDF…", "Export to PDF");
        // Without the extension where Explorer hides known ones (the Windows default).
        Assert.True(suggested is "notes.pdf" or "notes", $"The dialog suggested \"{suggested}\".");

        // A runner's first PDF can take a while; if none comes, say what the app is showing.
        try
        {
            await AppSession.WaitUntilAsync(() => StartsWithPdfHeader(pdf), "The PDF wasn't saved.", seconds: 60);
        }
        catch (TimeoutException)
        {
            var pdfs = Directory.GetFiles(_docs, "*.pdf", SearchOption.AllDirectories).Select(Path.GetFileName);
            throw new TimeoutException(
                $"The PDF wasn't saved. PDFs in the folder: [{string.Join(", ", pdfs)}]. The app shows: {OpenWindows(app)}");
        }

        // Its title is the file's name, not "MDGlint": as text, or as UTF-16 in hex.
        await AppSession.WaitUntilAsync(() => PdfTitle(pdf) is not null, "The PDF has no title.");
        Assert.Equal("notes.md", PdfTitle(pdf));
    }

    /// <summary>The PDF's /Title, written either as (text) or as &lt;FEFF…&gt; UTF-16 hex.</summary>
    private static string? PdfTitle(string path)
    {
        string text;
        try
        {
            text = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            return null; // Still being written.
        }

        var match = System.Text.RegularExpressions.Regex.Match(text, @"/Title\s*(\((?<text>[^)]*)\)|<(?<hex>[0-9A-Fa-f]+)>)");
        if (!match.Success)
        {
            return null;
        }

        if (match.Groups["text"].Success)
        {
            return match.Groups["text"].Value;
        }

        var bytes = Convert.FromHexString(match.Groups["hex"].Value);
        return bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF
            ? System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2)
            : System.Text.Encoding.Latin1.GetString(bytes);
    }

    /// <summary>The app's windows and the text in them, such as a dialog still open or an error.</summary>
    private static string OpenWindows(AppSession app)
    {
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, app.Process.Id));
        return string.Join(" | ", windows.Cast<AutomationElement>().Select(window =>
        {
            var texts = window.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                .Cast<AutomationElement>()
                .Select(text => text.Current.Name)
                .Where(text => text.Length > 0)
                .Take(10);
            return $"\"{window.Current.Name}\": {string.Join("; ", texts)}";
        }));
    }

    [EndToEndFact]
    public async Task LeavesOutTheBrowsersHeaderAndFooter()
    {
        await using var app = await AppSession.StartAsync(Write("notes.md", "# Notes\n\nSome text."));
        var page = app.Page;
        await page.WaitForAsync("document.querySelector('h1')?.textContent === 'Notes'");

        // With the header and footer asked for, the page prints no more text than without:
        // no date, title, address or page number.
        var plain = await PrintedTextRuns(page, headerAndFooter: false);
        Assert.True(plain > 0, "Nothing was printed.");
        Assert.Equal(plain, await PrintedTextRuns(page, headerAndFooter: true));

        // MDGlint's own footer instead: its name, and the page number of the total.
        Assert.Equal("\"MDGlint\" | counter(page) \" / \" counter(pages)", await page.EvaluateAsync<string>("""
            [...document.styleSheets].flatMap(sheet => [...sheet.cssRules])
              .filter(rule => rule instanceof CSSPageRule)
              .flatMap(rule => [...rule.cssRules])
              .filter(box => box.name === 'bottom-left' || box.name === 'bottom-right')
              .map(box => box.style.content).join(' | ')
            """));
    }

    [EndToEndFact]
    public async Task PrintsMDGlintsFooterOnEveryPage()
    {
        // A file long enough for several pages; emptying the footer's boxes must take away at
        // least its two pieces of text (name and page number) from every page.
        await using var app = await AppSession.StartAsync(Write("notes.md",
            "# Notes\n\n" + string.Concat(Enumerable.Range(1, 120).Select(i => $"Line {i}.\n\n"))));
        var page = app.Page;
        await page.WaitForAsync("document.querySelector('h1')?.textContent === 'Notes'");

        var withFooter = await PrintedTextRuns(page, headerAndFooter: false);
        var pages = (await PrintAsync(page, headerAndFooter: false)).Pages;
        Assert.True(pages > 1, $"The file printed on {pages} page.");
        await page.EvaluateAsync("""
            for (const sheet of document.styleSheets) {
              for (const rule of [...sheet.cssRules]) {
                if (rule instanceof CSSPageRule) {
                  for (const box of rule.cssRules) { box.style.content = '""'; }
                }
              }
            }
            """);
        var withoutFooter = await PrintedTextRuns(page, headerAndFooter: false);
        Assert.True(withFooter - withoutFooter >= 2 * pages,
            $"{pages} pages printed {withFooter} runs of text with the footer and {withoutFooter} without.");
    }

    /// <summary>The page printed to a PDF through DevTools, and how many pages it has.</summary>
    private static async Task<(byte[] Pdf, int Pages)> PrintAsync(DevTools page, bool headerAndFooter)
    {
        var result = await page.SendAsync("Page.printToPDF", new { displayHeaderFooter = headerAndFooter });
        var pdf = Convert.FromBase64String(result.GetProperty("data").GetString()!);
        var pages = System.Text.RegularExpressions.Regex.Matches(
            System.Text.Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;
        return (pdf, pages);
    }

    /// <summary>How many runs of text the page prints, counted in the PDF's content streams.</summary>
    private static async Task<int> PrintedTextRuns(DevTools page, bool headerAndFooter)
    {
        var (pdf, _) = await PrintAsync(page, headerAndFooter);
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var runs = 0;
        foreach (System.Text.RegularExpressions.Match stream in System.Text.RegularExpressions.Regex.Matches(text, @"(?<!end)stream\r?\n"))
        {
            var start = stream.Index + stream.Length;
            var end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0)
            {
                continue;
            }

            try
            {
                using var inflate = new System.IO.Compression.ZLibStream(
                    new MemoryStream(pdf, start, end - start), System.IO.Compression.CompressionMode.Decompress);
                using var content = new MemoryStream();
                inflate.CopyTo(content);
                runs += System.Text.RegularExpressions.Regex.Matches(
                    System.Text.Encoding.Latin1.GetString(content.ToArray()), @"\bT[jJ]\b").Count;
            }
            catch (InvalidDataException)
            {
                // Not a compressed stream (an image or font, say).
            }
        }

        return runs;
    }

    [EndToEndFact]
    public async Task OffersPrintExportAndCopyOnlyForAFile()
    {
        await using var app = await AppSession.StartAsync(null);
        await app.Page.WaitForAsync("document.querySelector('#mdglint-content .open-prompt') !== null");

        Assert.False((await app.MenuItemAsync("Print…")).Current.IsEnabled);
        Assert.False((await app.OpenMenuItemAsync("Export")).Current.IsEnabled);
        Assert.False((await app.OpenMenuItemAsync("Copy Markdown")).Current.IsEnabled);
    }

    private static Task<string> Background(DevTools page, string selector) =>
        page.EvaluateAsync<string>($"getComputedStyle(document.querySelector('{selector}')).backgroundColor");

    private static bool StartsWithPdfHeader(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[5];
            return stream.Read(header) == 5 && System.Text.Encoding.ASCII.GetString(header) == "%PDF-";
        }
        catch (IOException)
        {
            return false; // Still being written.
        }
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_docs, name);
        File.WriteAllText(path, text);
        return path;
    }
}
