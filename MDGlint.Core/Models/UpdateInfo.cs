namespace MDGlint.Core.Models;

/// <summary>
/// Where updates come from, and the names a release must use. The release workflow
/// (<c>.github/workflows/release.yml</c>) produces exactly these; every released version looks
/// for them, so they must never change (docs/decisions.md, "Updates keep working").
/// </summary>
public static class UpdateSource
{
    public const string Repository = "Jawad-Chughtai/MDGlint";
    public const string InstallerName = "MDGlintSetup.exe";
    public const string HashName = "MDGlintSetup.exe.sha256";

    /// <summary>GitHub's latest published release; drafts and pre-releases are never returned.</summary>
    public static Uri LatestReleaseApi { get; } = new($"https://api.github.com/repos/{Repository}/releases/latest");

    /// <summary>Release pages and downloads must be under these, or the release is ignored.</summary>
    public static string ReleasePagePrefix { get; } = $"https://github.com/{Repository}/releases/";
    public static string DownloadPrefix { get; } = $"https://github.com/{Repository}/releases/download/";
}

/// <summary>A newer release than the running version.</summary>
/// <param name="InstallerUrl">The installer, or null when the release can only be installed by
/// hand from its page (it has no installer or no <c>.sha256</c> file).</param>
public sealed record AvailableUpdate(
    SemanticVersion Version,
    Uri ReleasePage,
    Uri? InstallerUrl,
    long InstallerSize,
    Uri? HashUrl)
{
    /// <summary>MDGlint can download, verify and run this release's installer itself.</summary>
    public bool CanInstall => InstallerUrl is not null && HashUrl is not null;
}

public enum UpdateCheckStatus
{
    UpToDate,
    Available,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, AvailableUpdate? Update = null, string? Error = null)
{
    public static UpdateCheckResult UpToDate { get; } = new(UpdateCheckStatus.UpToDate);

    public static UpdateCheckResult Available(AvailableUpdate update) => new(UpdateCheckStatus.Available, update);

    public static UpdateCheckResult Failed(string error) => new(UpdateCheckStatus.Failed, Error: error);
}

/// <summary>
/// A downloaded installer whose SHA-256 matched its release. It stays open, so nothing can change
/// it, until disposed: start it first, then dispose.
/// </summary>
public sealed class VerifiedInstaller(string path, SemanticVersion version, IDisposable fileLock) : IDisposable
{
    public string Path { get; } = path;

    public SemanticVersion Version { get; } = version;

    public void Dispose() => fileLock.Dispose();
}

/// <summary>Downloading or checking an update's installer failed; the message says why.</summary>
public sealed class UpdateDownloadException(string message, Exception? inner = null) : Exception(message, inner);
