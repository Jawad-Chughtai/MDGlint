using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using MDGlint.Core.Models;
using MDGlint.Desktop.Helpers;
using Microsoft.Win32;

namespace MDGlint.Desktop.Services;

/// <summary>The Windows side of updates: setup's choice, old downloads, running setup, the HTTP client.</summary>
public static class UpdateLauncher
{
    /// <summary>Setup closes MDGlint if needed, installs quietly and opens MDGlint again ([Run] in installer.iss).</summary>
    public const string InstallerArguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /UPDATE=1";

    /// <summary>Debug builds only: <c>http://localhost:PORT</c> receives every update request instead.</summary>
    public const string DebugUpdateServerVariable = "MDGLINT_DEBUG_UPDATE_SERVER";

    /// <summary>The key setup writes (installer.iss); under HKCU per user, HKLM for all users.</summary>
    public const string SetupKey = @"Software\MDGlint";

    /// <summary>
    /// The "Check for updates" choice made in setup, per user first, then all users; null when
    /// MDGlint wasn't installed by a setup that asked.
    /// </summary>
    public static bool? ReadSetupChoice() => ReadSetupChoice(SetupKey, Registry.CurrentUser, Registry.LocalMachine);

    internal static bool? ReadSetupChoice(string keyPath, params RegistryKey[] roots)
    {
        foreach (var root in roots)
        {
            try
            {
                using var key = root.OpenSubKey(keyPath);
                if (key?.GetValue("CheckForUpdates") is int value)
                {
                    return value != 0;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Unreadable: try the next one.
            }
        }

        return null;
    }

    /// <summary>Deletes installers left from earlier updates.</summary>
    public static void DeleteOldDownloads(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still in use (setup may still be finishing); the next start tries again.
        }
    }

    /// <summary>
    /// Starts the verified installer, then releases it. Returns null when it started, or why it
    /// couldn't (the user declined the administrator prompt, for example).
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = ProcessLauncher.UnitTestsCannotLaunch)]
    public static string? StartInstaller(VerifiedInstaller installer) => StartInstaller(installer, ProcessLauncher.Start);

    internal static string? StartInstaller(VerifiedInstaller installer, Action<ProcessStartInfo> start)
    {
        using (installer)
        {
            try
            {
                start(new ProcessStartInfo(installer.Path, InstallerArguments) { UseShellExecute = true });
                return null;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
            {
                return ex.Message;
            }
        }
    }

    public static HttpClient CreateHttpClient() => CreateHttpClient(
#if DEBUG
        Environment.GetEnvironmentVariable(DebugUpdateServerVariable),
#else
        null,
#endif
        new SocketsHttpHandler { AllowAutoRedirect = true, ConnectTimeout = TimeSpan.FromSeconds(15) });

    internal static HttpClient CreateHttpClient(string? debugServer, HttpMessageHandler handler)
    {
        if (Uri.TryCreate(debugServer, UriKind.Absolute, out var server) && server.IsLoopback)
        {
            // Only reachable from Debug builds (see CreateHttpClient above): tries the update flow
            // against a local fake server.
            handler = new RedirectHandler(server, handler);
        }

        // Checks pass their own timeout; a download can take as long as the connection needs.
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Sends <c>https://host/path</c> to <c>server/host/path</c>.</summary>
    internal sealed class RedirectHandler(Uri server, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var original = request.RequestUri!;
            request.RequestUri = new UriBuilder(server)
            {
                Path = original.Host + original.AbsolutePath,
                Query = original.Query.TrimStart('?'),
            }.Uri;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
