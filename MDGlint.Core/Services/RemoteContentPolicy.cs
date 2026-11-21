namespace MDGlint.Core.Services;

/// <summary>
/// Decides whether a document may load content from the internet (images, video, audio, and URLs
/// in inline styles). Blocked unless the user allowed it for that file, for this session, or for
/// every file. Loading remote content tells its server that, and when, a file was opened.
/// </summary>
public sealed class RemoteContentPolicy
{
    private readonly HashSet<string> _allowedFiles = new(StringComparer.OrdinalIgnoreCase);

    public RemoteContentPolicy(bool alwaysAllow)
    {
        AlwaysAllow = alwaysAllow;
    }

    /// <summary>The saved "Always allow" choice.</summary>
    public bool AlwaysAllow { get; private set; }

    public bool IsAllowed(string? filePath)
    {
        if (AlwaysAllow)
        {
            return true;
        }

        var key = TryNormalize(filePath);
        return key is not null && _allowedFiles.Contains(key);
    }

    /// <summary>"Load for this file": allowed until MDGlint closes or remote content is turned off.</summary>
    public void AllowForFile(string filePath)
    {
        var key = TryNormalize(filePath) ?? throw new ArgumentException("Not a valid file path.", nameof(filePath));
        _allowedFiles.Add(key);
    }

    /// <summary>Turning it off also forgets the per-file choices, so off means off.</summary>
    public void SetAlwaysAllow(bool alwaysAllow)
    {
        AlwaysAllow = alwaysAllow;
        if (!alwaysAllow)
        {
            _allowedFiles.Clear();
        }
    }

    /// <summary>
    /// True for <c>http</c> and <c>https</c> addresses on any host other than
    /// <paramref name="localHosts"/> (the app's own virtual hosts).
    /// </summary>
    public static bool IsRemote(Uri uri, params string[] localHosts)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(localHosts);

        return uri.IsAbsoluteUri
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !localHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    private static string? TryNormalize(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
