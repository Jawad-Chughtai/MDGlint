using System.Windows;
using System.Windows.Automation;

namespace MDGlint.EndToEnd.Tests;

/// <summary>Copying: a code block's Copy button, and Copy Markdown for the whole file.</summary>
public sealed class CopyTests : IDisposable
{
    private const string Before = "MDGlint test: not copied yet";

    private readonly string _docs = AppSession.NewFolder();

    public void Dispose() => Directory.Delete(_docs, recursive: true);

    [EndToEndFact]
    public async Task CopiesACodeBlock()
    {
        var file = Write("notes.md", "# Code\n\n```js\nlet x = 1;\nlet y = 2;\n```\n\n```\nplain\n```\n");
        await using var app = await AppSession.StartAsync(file);
        var page = app.Page;
        await page.WaitForAsync("document.querySelectorAll('#mdglint-content .code-block > .copy-code').length === 2",
            "The code blocks got no Copy buttons.");
        SetClipboard(Before);

        await page.ClickAsync("#mdglint-content > .code-block:nth-child(2) .copy-code");

        // The highlighted block's code as written, without the closing newline. (Chromium puts
        // Windows line endings on the clipboard.)
        await AppSession.WaitUntilAsync(() => ClipboardText().ReplaceLineEndings("\n") == "let x = 1;\nlet y = 2;",
            "The code wasn't copied.");
        Assert.Equal("Copied", await page.EvaluateAsync<string>(
            "document.querySelector('#mdglint-content > .code-block:nth-child(2) .copy-code').getAttribute('aria-label')"));

        await page.ClickAsync("#mdglint-content > .code-block:nth-child(3) .copy-code");
        await AppSession.WaitUntilAsync(() => ClipboardText() == "plain", "The second block wasn't copied.");

        // Back to "Copy" after a moment.
        await page.WaitForAsync(
            "document.querySelector('#mdglint-content > .code-block:nth-child(2) .copy-code').getAttribute('aria-label') === 'Copy'",
            "The button kept saying Copied.");
    }

    [EndToEndFact]
    public async Task CopiesTheWholeFilesMarkdown()
    {
        const string Markdown = "# Notes\r\n\r\nSome *text* and `code`.\r\n";
        var file = Write("notes.md", Markdown);
        await using var app = await AppSession.StartAsync(file);
        await app.Page.WaitForAsync("document.querySelector('h1')?.textContent === 'Notes'");
        SetClipboard(Before);

        var item = await app.MenuItemAsync("Copy Markdown");
        ((InvokePattern)item.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

        // The file as it is, not the rendered text.
        await AppSession.WaitUntilAsync(() => ClipboardText() == Markdown, "The Markdown wasn't copied.");
    }

    /// <summary>The clipboard is used from a thread of its own, as it needs one in single-threaded mode.</summary>
    private static T OnClipboardThread<T>(Func<T> action)
    {
        var result = default(T);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    result = action();
                    return;
                }
                catch (System.Runtime.InteropServices.ExternalException ex) when (attempt < 20)
                {
                    failure = ex;
                    Thread.Sleep(50); // Another program has the clipboard open.
                }
                catch (Exception ex)
                {
                    failure = ex;
                    return;
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result is null && failure is not null ? throw failure : result!;
    }

    private static void SetClipboard(string text) => OnClipboardThread(() =>
    {
        Clipboard.SetText(text);
        return true;
    });

    private static string ClipboardText() => OnClipboardThread(() => Clipboard.ContainsText() ? Clipboard.GetText() : "");

    private string Write(string name, string text)
    {
        var path = Path.Combine(_docs, name);
        File.WriteAllText(path, text);
        return path;
    }
}
