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

        var export = await app.MenuItemAsync("Export");
        ((ExpandCollapsePattern)export.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        ((InvokePattern)(await app.OpenMenuItemAsync("PDF…")).GetCurrentPattern(InvokePattern.Pattern)).Invoke();

        await AppSession.WaitUntilAsync(() => app.FindWindow("Export to PDF") is not null, "The Save dialog didn't appear.");
        var dialog = app.FindWindow("Export to PDF")!;
        var name = dialog.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "1001")));
        // Without the extension where Explorer hides known ones (the Windows default).
        Assert.Contains(((ValuePattern)name.GetCurrentPattern(ValuePattern.Pattern)).Current.Value, new[] { "notes.pdf", "notes" });
        var save = dialog.FindFirst(TreeScope.Children, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "1")));
        ((InvokePattern)save.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

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
    public async Task OffersPrintAndExportOnlyForAFile()
    {
        await using var app = await AppSession.StartAsync(null);
        await app.Page.WaitForAsync("document.querySelector('#mdglint-content .open-prompt') !== null");

        Assert.False((await app.MenuItemAsync("Print…")).Current.IsEnabled);
        Assert.False((await app.OpenMenuItemAsync("Export")).Current.IsEnabled);
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
