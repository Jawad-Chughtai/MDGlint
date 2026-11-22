using System.Windows.Automation;

namespace MDGlint.EndToEnd.Tests;

/// <summary>What the preview shows, loads and refuses, in the real WebView2.</summary>
public sealed class PreviewTests : IDisposable
{
    internal static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private readonly string _docs = AppSession.NewFolder();

    public void Dispose() => Directory.Delete(_docs, recursive: true);

    [EndToEndFact]
    public async Task ShowsTheFileWithoutRunningItsScripts()
    {
        var file = Write("notes.md", """
            # Hello

            <script>window.ranScript = true</script>
            <img src="missing.png" onerror="window.ranHandler = true">
            <a href="javascript:window.ranLink = true" id="js">js link</a>

            Some **bold** text.
            """);

        await using var app = await AppSession.StartAsync(file);
        var page = app.Page;

        Assert.Equal("Hello", await page.EvaluateAsync<string>("document.querySelector('#mdglint-content h1').textContent"));
        Assert.Equal("bold", await page.EvaluateAsync<string>("document.querySelector('#mdglint-content strong').textContent"));
        Assert.Equal(0, await page.EvaluateAsync<int>("document.querySelectorAll('#mdglint-content script').length"));
        Assert.False(await page.EvaluateAsync<bool>("document.querySelector('#mdglint-content img').hasAttribute('onerror')"));
        Assert.True(await page.EvaluateAsync<bool>("window.ranScript === undefined && window.ranHandler === undefined"));

        // The page's Content-Security-Policy stops inline script even if some got through.
        Assert.True(await page.EvaluateAsync<bool>("""
            (() => {
              const script = document.createElement('script');
              script.textContent = 'window.ranInline = true';
              document.body.append(script);
              return window.ranInline === undefined;
            })()
            """));
    }

    [EndToEndFact]
    public async Task LoadsLocalImagesButNoOtherFiles()
    {
        File.WriteAllBytes(Path.Combine(_docs, "relative.png"), Png);
        Directory.CreateDirectory(Path.Combine(_docs, "img"));
        File.WriteAllBytes(Path.Combine(_docs, "img", "absolute.png"), Png);
        File.WriteAllBytes(Path.Combine(_docs, "up.png"), Png);
        Write(@"notes\secret.txt", "not for the page");
        var file = Write(@"notes\notes.md", "# Images");
        var absolute = Path.Combine(_docs, "img", "absolute.png");

        await using var app = await AppSession.StartAsync(file);
        var page = app.Page;
        await page.SendAsync("Network.enable");

        Write(@"notes\notes.md", $"""
            # Images

            ![relative](../relative.png) ![up](../up.png)

            ![absolute]({absolute})

            ![file]({new Uri(absolute).AbsoluteUri})

            ![secret](secret.txt)
            """);
        await page.WaitForAsync("document.images.length === 5 && Array.from(document.images).every(i => i.complete)");

        var widths = await page.EvaluateAsync<int[]>(
            "['relative', 'up', 'absolute', 'file', 'secret'].map(a => document.querySelector(`img[alt=${a}]`).naturalWidth)");
        Assert.True(widths is [1, 1, 1, 1, 0], await page.DescribeAsync());
        Assert.StartsWith("https://doc.mdglint.example/", await page.EvaluateAsync<string>("document.querySelector('img[alt=absolute]').src"));

        // The text file was asked for and refused, not read.
        var secret = page.Events.Where(e => e.Method == "Network.responseReceived")
            .Select(e => e.Params.GetProperty("response"))
            .Single(r => r.GetProperty("url").GetString()!.EndsWith("/secret.txt", StringComparison.Ordinal));
        Assert.Equal(404, secret.GetProperty("status").GetInt32());

        // Script can't read local files, even images.
        Assert.Equal("blocked", await page.EvaluateAsync<string>(
            "fetch(document.querySelector('img[alt=relative]').src).then(() => 'read', () => 'blocked')"));
    }

    [EndToEndFact]
    public async Task OpensClickedLinksOutsideThePreview()
    {
        Directory.CreateDirectory(Path.Combine(_docs, "folder"));
        File.WriteAllText(Path.Combine(_docs, "tool.exe"), "not a real program");
        Write("other.md", "# Other file");
        var file = Write("notes.md", """
            # Links

            [web](https://example.com/page) [program](tool.exe) [folder](folder/) [markdown](other.md)
            """);

        await using var app = await AppSession.StartAsync(file);
        var page = app.Page;

        await page.ClickAsync("a[href='https://example.com/page']");
        await AppSession.WaitUntilAsync(() => app.Launches().Length == 1, "The web link didn't open.");
        Assert.Equal("https://example.com/page|", app.Launches()[0]);

        // A program is shown in Explorer, never run.
        await page.ClickAsync("a[href='tool.exe']");
        await AppSession.WaitUntilAsync(() => app.Launches().Length == 2, "The program link did nothing.");
        Assert.Equal($"explorer.exe|/select,\"{Path.Combine(_docs, "tool.exe")}\"", app.Launches()[1]);

        await page.ClickAsync("a[href='folder/']");
        await AppSession.WaitUntilAsync(() => app.Launches().Length == 3, "The folder link did nothing.");
        Assert.StartsWith($"explorer.exe|/select,\"{Path.Combine(_docs, "folder")}", app.Launches()[2]);

        // Navigation the page starts on its own goes nowhere.
        await page.EvaluateAsync("location.href = 'https://example.com/script'; true");
        await page.EvaluateAsync("window.open('https://example.com/popup'); true");
        await Task.Delay(1000);
        Assert.Equal(3, app.Launches().Length);
        Assert.Equal("Links", await page.EvaluateAsync<string>("document.querySelector('h1').textContent"));

        // Markdown opens in MDGlint itself, at the top.
        await page.ClickAsync("a[href='other.md']");
        await page.WaitForAsync("document.querySelector('h1')?.textContent === 'Other file'");
        Assert.Equal(3, app.Launches().Length);
    }

    [EndToEndFact]
    public async Task OpensMarkdownFilesDroppedOnThePreview()
    {
        var image = Path.Combine(_docs, "picture.png");
        File.WriteAllBytes(image, Png);
        var text = Write("notes.txt", "Not Markdown");
        var dropped = Write("dropped.md", "# Dropped file");
        var file = Write("notes.md", "# Original");

        await using var app = await AppSession.StartAsync(file);
        var page = app.Page;

        // Files that aren't Markdown are ignored, and the page doesn't navigate to them.
        await page.DropFilesAsync(image, text);
        await Task.Delay(1000);
        Assert.Equal("Original", await page.EvaluateAsync<string>("document.querySelector('h1').textContent"));
        Assert.Empty(app.Launches());

        // With several, the first Markdown file opens.
        await page.DropFilesAsync(image, dropped, file);
        await page.WaitForAsync("document.querySelector('h1')?.textContent === 'Dropped file'");
        Assert.Empty(app.Launches());
    }

    [EndToEndFact]
    public async Task OffersOpenOnlyWhenNoFileIsOpen()
    {
        const string Dialog = "Open Markdown File";

        // With a file open there is no prompt, and a file can't add a button of its own.
        var file = Write("notes.md", """
            # Notes

            <div class="open-prompt"><button type="button">Open…</button></div>
            """);
        await using (var withFile = await AppSession.StartAsync(file))
        {
            await withFile.Page.WaitForAsync("document.querySelector('h1')?.textContent === 'Notes'");
            Assert.Equal(0, await withFile.Page.EvaluateAsync<int>("document.querySelectorAll('#mdglint-content button').length"));
        }

        await using var app = await AppSession.StartAsync(null);
        var page = app.Page;
        await page.WaitForAsync("document.querySelector('#mdglint-content .open-prompt button') !== null");
        Assert.Equal("Open a Markdown file to preview it.", await page.EvaluateAsync<string>(
            "document.querySelector('#mdglint-content > p.message').textContent"));
        Assert.Equal("Or drop a Markdown file here.", await page.EvaluateAsync<string>(
            "document.querySelector('#mdglint-content .open-prompt .message').textContent"));

        await page.ClickAsync("#mdglint-content .open-prompt button");
        await AppSession.WaitUntilAsync(() => app.FindWindow(Dialog) is not null, "The Open dialog didn't appear.");
        ((WindowPattern)app.FindWindow(Dialog)!.GetCurrentPattern(WindowPattern.Pattern)).Close();
        await AppSession.WaitUntilAsync(() => app.FindWindow(Dialog) is null, "The Open dialog didn't close.");
    }

    [EndToEndFact]
    public async Task JumpsToHeadingsWithinTheFile()
    {
        var filler = string.Join("\n\n", Enumerable.Range(1, 80).Select(i => $"Paragraph {i}."));
        var file = Write("notes.md", $"""
            # Top

            [Go to the end](#the-end)

            {filler}

            ## The end

            [Back up](#)

            {filler}
            """);

        await using var app = await AppSession.StartAsync(file);
        var page = app.Page;

        await page.ClickAsync("a[href='#the-end']");
        await page.WaitForAsync("Math.abs(document.getElementById('the-end').getBoundingClientRect().top) < 2");
        Assert.Empty(app.Launches());

        await page.ClickAsync("a[href='#']");
        await page.WaitForAsync("window.scrollY === 0");
    }

    [EndToEndFact]
    public async Task KeepsTheReadersPlaceWhenTheFileIsSaved()
    {
        string Document(int extraInSectionTwo) => "# Notes\n\n" + string.Join("\n\n", Enumerable.Range(1, 12).Select(section =>
            $"## Section {section}\n\n" + string.Join("\n\n",
                Enumerable.Range(1, 6 + (section == 2 ? extraInSectionTwo : 0)).Select(p => $"Paragraph {section}.{p} of the notes."))));
        const string Target = "[...document.querySelectorAll('p')].find(p => p.textContent === 'Paragraph 8.3 of the notes.')";

        var file = Write("notes.md", Document(0));
        await using var app = await AppSession.StartAsync(file);
        var page = app.Page;

        await page.EvaluateAsync($"window.scrollTo(0, {Target}.getBoundingClientRect().top + window.scrollY - 40); true");
        var before = await page.EvaluateAsync<double>($"{Target}.getBoundingClientRect().top");
        var scrolled = await page.EvaluateAsync<double>("window.scrollY");

        // Paragraphs added above the reader, as an editor saving the file would.
        Write("notes.md", Document(10));
        await page.WaitForAsync("document.querySelectorAll('p').length === 82");

        Assert.Equal(before, await page.EvaluateAsync<double>($"{Target}.getBoundingClientRect().top"), 1.0);
        Assert.True(await page.EvaluateAsync<double>("window.scrollY") > scrolled);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_docs, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
}
