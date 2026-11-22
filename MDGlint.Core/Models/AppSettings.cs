namespace MDGlint.Core.Models;

/// <summary>
/// What MDGlint remembers between runs, stored as <c>settings.json</c>. Every version must read
/// the file any older version wrote: add settings, but never rename or remove one without
/// migrating it (docs/decisions.md, "Updates keep working").
/// </summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = ThemeKind.Light.ToStorageValue();

    /// <summary>Load remote images, video and audio in every file. Off by default.</summary>
    public bool AllowRemoteContent { get; set; }

    /// <summary>
    /// Print and export to PDF in the light theme, whatever the window shows. On by default, which
    /// also applies to settings files written before 1.1, which don't have it.
    /// </summary>
    public bool PrintInLightTheme { get; set; } = true;

    /// <summary>
    /// The user's in-app choice about checking for updates; null until they make one, when the
    /// choice made in setup applies (see <see cref="UpdatePreference"/>).
    /// </summary>
    public bool? CheckForUpdates { get; set; }

    /// <summary>
    /// Also offer pre-releases (<c>x.y.z-beta.n</c>). Off by default, which also applies to
    /// settings files written before 1.1, which don't have it.
    /// </summary>
    public bool GetBetaVersions { get; set; }

    /// <summary>When MDGlint last checked for updates on its own.</summary>
    public DateTimeOffset? LastUpdateCheck { get; set; }

    /// <summary>Whether the window opens maximized, as it does until the user restores it.</summary>
    public bool WindowMaximized { get; set; } = true;

    /// <summary>
    /// The window's normal (not maximized) position and size when it last closed, in device-independent
    /// pixels; null until it has closed once, when it opens centred at its default size.
    /// </summary>
    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }
}
