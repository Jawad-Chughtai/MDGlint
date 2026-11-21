namespace MDGlint.Desktop.Helpers;

/// <summary>Extra command-line arguments for the WebView2 browser process.</summary>
public static class BrowserArguments
{
    /// <summary>
    /// Debug builds only: arguments passed to WebView2 through its API, such as the
    /// <c>--remote-debugging-port</c> the end-to-end tests connect to. WebView2 ignores its own
    /// <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS</c> variable when the app runs elevated, as it does
    /// on CI runners, but always honours arguments set through the API (docs/testing.md).
    /// </summary>
    public const string DebugVariable = "MDGLINT_DEBUG_BROWSER_ARGUMENTS";

    /// <summary>The arguments to pass, or null for none (always null in Release builds).</summary>
    public static string? Get() => Resolve(
#if DEBUG
        Environment.GetEnvironmentVariable(DebugVariable)
#else
        null
#endif
    );

    internal static string? Resolve(string? debugArguments) =>
        string.IsNullOrWhiteSpace(debugArguments) ? null : debugArguments.Trim();
}
