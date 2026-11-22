using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Windows.Automation;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MDGlint.EndToEnd.Tests;

/// <summary>Only Debug builds have the test hooks these tests rely on.</summary>
internal sealed class EndToEndFactAttribute : FactAttribute
{
    public EndToEndFactAttribute()
    {
#if !DEBUG
        Skip = "The end-to-end tests drive the Debug build, which has the test hooks (docs/testing.md).";
#endif
    }
}

internal sealed record AppOptions
{
    /// <summary>settings.json before the app starts; by default no update checks.</summary>
    public string Settings { get; init; } = """{"CheckForUpdates":false}""";

    public string? UpdateServer { get; init; }

    /// <summary>False when the preview isn't expected to start (no WebView2).</summary>
    public bool ConnectToPreview { get; init; } = true;

    public Dictionary<string, string> Environment { get; init; } = [];

    /// <summary>Runs with the session's folder just before the app starts.</summary>
    public Action<string>? BeforeStart { get; init; }
}

/// <summary>
/// One run of the app in a folder of its own: its settings and WebView2 data live there (the
/// Debug build's data-folder hook), programs it would start are written to a launch log instead,
/// and the preview is reachable over DevTools.
/// </summary>
internal sealed class AppSession : IAsyncDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    private AppSession(string folder) => Folder = folder;

    public string Folder { get; }

    public string LaunchLog => Path.Combine(Folder, "launches.txt");

    public string SettingsFile => Path.Combine(Folder, "data", "Roaming", "MDGlint", "settings.json");

    public string UpdatesFolder => Path.Combine(Folder, "data", "Local", "MDGlint", "Updates");

    public Process Process { get; private set; } = null!;

    public AutomationElement Window { get; private set; } = null!;

    public DevTools Page { get; private set; } = null!;

    /// <summary>The app's WebView2 data folder (the Debug build's data-folder hook puts it here).</summary>
    public string WebView2Folder => Path.Combine(Folder, "data", "Local", "MDGlint", "WebView2");

    private int _port;

    /// <summary>Connects to the preview once it has started, for a session started without connecting.</summary>
    public async Task ConnectToPreviewAsync()
    {
        Page = await DevTools.ConnectAsync(_port, "https://app.mdglint.example/", StartTimeout);
        await Page.WaitForAsync("window.MDGlintPreview && document.readyState === 'complete'", "The preview page didn't load.", 30);
    }

    public static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MDGlintE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static async Task<AppSession> StartAsync(string? file, AppOptions? options = null)
    {
        options ??= new AppOptions();
        var session = new AppSession(NewFolder());
        Directory.CreateDirectory(Path.GetDirectoryName(session.SettingsFile)!);
        File.WriteAllText(session.SettingsFile, options.Settings);

        var port = FreePort();
        session._port = port;
        options.BeforeStart?.Invoke(session.Folder);
        var start = new ProcessStartInfo(AppPath()) { UseShellExecute = false };
        if (file is not null)
        {
            start.ArgumentList.Add(file);
        }

        start.Environment["MDGLINT_DEBUG_DATA_FOLDER"] = Path.Combine(session.Folder, "data");
        start.Environment["MDGLINT_DEBUG_LAUNCH_LOG"] = session.LaunchLog;
        // Through the app's Debug hook, not WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: WebView2 ignores
        // that variable when the app runs elevated, as everything does on CI runners.
        start.Environment["MDGLINT_DEBUG_BROWSER_ARGUMENTS"] = $"--remote-debugging-port={port}";
        if (options.UpdateServer is not null)
        {
            start.Environment["MDGLINT_DEBUG_UPDATE_SERVER"] = options.UpdateServer;
        }

        foreach (var (name, value) in options.Environment)
        {
            start.Environment[name] = value;
        }

        session.Process = Process.Start(start)!;
        try
        {
            session.Window = await WaitForWindowAsync(session.Process);
            if (options.ConnectToPreview)
            {
                session.Page = await DevTools.ConnectAsync(port, "https://app.mdglint.example/", StartTimeout);
                await session.Page.WaitForAsync("window.MDGlintPreview && document.readyState === 'complete'", "The preview page didn't load.", 30);
                if (file is not null)
                {
                    await session.Page.WaitForAsync("!document.body.hidden", "The file wasn't shown.", 30);
                }
            }
        }
        catch (TimeoutException ex) when (session.Window is not null)
        {
            // Say what the window shows, such as the "preview couldn't start" notice and its error.
            var shown = WindowText(session.Window);
            var webView = WebViewState(session.Folder);
            await session.DisposeAsync();
            throw new TimeoutException($"{ex.Message} The window shows: {shown}{Environment.NewLine}{webView}", ex);
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }

        return session;
    }

    /// <summary>What WebView2 left behind: its processes, their command lines, and the port it chose.</summary>
    private static string WebViewState(string folder)
    {
        var lines = new List<string>
        {
            $"msedgewebview2 processes running: {Process.GetProcessesByName("msedgewebview2").Length}",
        };

        // The browser process's command line shows whether the extra arguments reached it.
        try
        {
            const string Script = "Get-CimInstance Win32_Process -Filter \"Name='msedgewebview2.exe'\" | "
                + "Where-Object { $_.CommandLine -notmatch '--type=' } | ForEach-Object { $_.CommandLine }";
            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(Script));
            var query = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -EncodedCommand {encoded}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            })!;
            var browsers = query.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            query.WaitForExit();
            lines.Add($"Browser processes ({browsers.Length}):");
            lines.AddRange(browsers.Select(b => "  " + b.Trim()));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            lines.Add($"Couldn't read the browser command lines: {ex.Message}");
        }
        foreach (var file in Directory.EnumerateFiles(folder, "DevToolsActivePort", SearchOption.AllDirectories))
        {
            lines.Add($"{Path.GetRelativePath(folder, file)}: {File.ReadAllText(file).ReplaceLineEndings(" ")}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string WindowText(AutomationElement window)
    {
        try
        {
            var names = window.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Select(e => e.Current.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct();
            return "\"" + string.Join("\" | \"", names) + "\"";
        }
        catch (ElementNotAvailableException)
        {
            return "(the window closed)";
        }
    }

    /// <summary>The Debug build of the app, next to this repository's test projects.</summary>
    public static string AppPath()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "MDGlint.slnx")))
        {
            folder = folder.Parent;
        }

        var path = Path.Combine(folder!.FullName, "MDGlint.Desktop", "bin", "Debug", "net10.0-windows", "win-x64", "MDGlint.Desktop.exe");
        Assert.True(File.Exists(path), $"Build the app in Debug first: {path}");
        return path;
    }

    /// <summary>
    /// The programs the app started so far. Read alongside the app, which may be appending a line
    /// right now: only finished lines count.
    /// </summary>
    public string[] Launches()
    {
        if (!File.Exists(LaunchLog))
        {
            return [];
        }

        using var stream = new FileStream(LaunchLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var text = new StreamReader(stream).ReadToEnd();
        var finished = text[..(text.LastIndexOf('\n') + 1)];
        return finished.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    // UI Automation: elements are found by their x:Name, which WPF exposes as the AutomationId.

    public AutomationElement? Find(string automationId) =>
        Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

    public async Task<AutomationElement> WaitForAsync(string automationId, double seconds = 15)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (true)
        {
            var element = Find(automationId);
            if (element is not null)
            {
                return element;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"{automationId} didn't appear.");
            }

            await Task.Delay(100);
        }
    }

    public async Task InvokeAsync(string automationId) =>
        ((InvokePattern)(await WaitForAsync(automationId)).GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    /// <summary>The text of a TextBlock.</summary>
    public string Text(string automationId) => Find(automationId)?.Current.Name ?? string.Empty;

    public async Task<string> WaitForTextAsync(string automationId, Func<string, bool> condition, double seconds = 15)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (true)
        {
            var text = Text(automationId);
            if (condition(text))
            {
                return text;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"{automationId} still says \"{text}\".");
            }

            await Task.Delay(100);
        }
    }

    /// <summary>A window of the app's other than its main one, such as a dialog, by its title.</summary>
    public AutomationElement? FindWindow(string name) =>
        AutomationElement.RootElement.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, Process.Id),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
            new PropertyCondition(AutomationElement.NameProperty, name)));

    /// <summary>
    /// Opens ⋯ ▸ Export ▸ <paramref name="format"/> and saves under the name the Save dialog
    /// suggests, which is returned. (Typing a name through UI Automation doesn't register on the
    /// CI runner, so the tests keep the suggestion.)
    /// </summary>
    public async Task<string> ExportWithSuggestedNameAsync(string format, string dialogTitle)
    {
        var export = await MenuItemAsync("Export");
        ((ExpandCollapsePattern)export.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        ((InvokePattern)(await OpenMenuItemAsync(format)).GetCurrentPattern(InvokePattern.Pattern)).Invoke();

        await WaitUntilAsync(() => FindWindow(dialogTitle) is not null, "The Save dialog didn't appear.");
        var dialog = FindWindow(dialogTitle)!;
        var name = dialog.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "1001")));
        // The box can still be empty for a moment after the dialog appears.
        string Suggested() => ((ValuePattern)name.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
        await WaitUntilAsync(() => Suggested().Length > 0, "The dialog suggested no name.");
        var suggested = Suggested();
        var save = dialog.FindFirst(TreeScope.Children, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "1")));
        ((InvokePattern)save.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        return suggested;
    }

    /// <summary>Opens the ⋯ menu and returns one of its items (the menu is a window of its own).</summary>
    public async Task<AutomationElement> MenuItemAsync(string name)
    {
        await InvokeAsync("MoreButton");
        return await OpenMenuItemAsync(name);
    }

    /// <summary>An item of a menu that is already open, such as a submenu's.</summary>
    public async Task<AutomationElement> OpenMenuItemAsync(string name)
    {
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, Process.Id),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
            new PropertyCondition(AutomationElement.NameProperty, name));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var item = AutomationElement.RootElement.FindFirst(TreeScope.Descendants, condition);
            if (item is not null)
            {
                return item;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"The menu item \"{name}\" didn't appear.");
            }

            await Task.Delay(100);
        }
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string because, double seconds = 15)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(because);
            }

            await Task.Delay(100);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Page is not null)
        {
            await Page.DisposeAsync();
        }

        if (Process is not null)
        {
            if (!Process.HasExited)
            {
                Process.CloseMainWindow();
                if (!Process.WaitForExit(10_000))
                {
                    Process.Kill(entireProcessTree: true);
                }
            }

            Process.Dispose();
        }

        // WebView2's own processes can hold the data folder for a moment after the app exits.
        for (var attempt = 0; attempt < 20 && Directory.Exists(Folder); attempt++)
        {
            try
            {
                Directory.Delete(Folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(250);
            }
        }
    }

    private static async Task<AutomationElement> WaitForWindowAsync(Process process)
    {
        var deadline = DateTime.UtcNow + StartTimeout;
        while (true)
        {
            process.Refresh();
            if (process.HasExited)
            {
                throw new InvalidOperationException($"MDGlint exited at start ({process.ExitCode}).");
            }

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return AutomationElement.FromHandle(process.MainWindowHandle);
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("MDGlint's window didn't appear.");
            }

            await Task.Delay(100);
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
