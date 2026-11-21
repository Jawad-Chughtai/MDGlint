namespace MDGlint.Core.Models;

/// <summary>The version shown to users, from the one set in <c>Directory.Build.props</c>.</summary>
public static class AppVersion
{
    /// <summary>
    /// Turns an assembly's informational version into the text shown to users. The SDK may add
    /// build metadata after a <c>+</c> (the source commit, as in <c>1.0.0+3f2a1b9</c>), which is
    /// dropped. Returns an empty string when there is no version.
    /// </summary>
    public static string ToDisplay(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return string.Empty;
        }

        var metadataStart = informationalVersion.IndexOf('+');
        return (metadataStart >= 0 ? informationalVersion[..metadataStart] : informationalVersion).Trim();
    }
}
