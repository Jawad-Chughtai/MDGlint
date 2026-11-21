namespace MDGlint.Core.Models;

/// <summary>
/// Decides whether MDGlint checks for updates on its own. Checks are opt-in: the user's in-app
/// choice wins, then the one made in setup, and with neither MDGlint never checks.
/// </summary>
public static class UpdatePreference
{
    /// <summary>How long MDGlint waits between checks of its own.</summary>
    public static TimeSpan CheckInterval { get; } = TimeSpan.FromDays(1);

    public static bool IsEnabled(AppSettings settings, bool? setupChoice)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.CheckForUpdates ?? setupChoice ?? false;
    }

    /// <summary>
    /// True when checks are on and the last one was at least <see cref="CheckInterval"/> ago (or
    /// never happened, or is dated in the future because the clock was changed).
    /// </summary>
    public static bool IsCheckDue(AppSettings settings, bool? setupChoice, DateTimeOffset now)
    {
        if (!IsEnabled(settings, setupChoice))
        {
            return false;
        }

        return settings.LastUpdateCheck is not { } last || last > now || now - last >= CheckInterval;
    }
}
