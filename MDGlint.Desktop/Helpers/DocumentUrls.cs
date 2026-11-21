using System.IO;

namespace MDGlint.Desktop.Helpers;

/// <summary>
/// Converts between local paths and preview URLs on the document host. The URL path spells out
/// the full local path, so relative links (including <c>../</c>) resolve the same way they do on
/// disk:
/// <list type="bullet">
///   <item><c>C:\Users\me\docs\</c> ↔ <c>https://doc.mdglint.example/C/Users/me/docs/</c></item>
///   <item><c>\\server\share\docs\</c> ↔ <c>https://doc.mdglint.example/UNC/server/share/docs/</c></item>
/// </list>
/// </summary>
public static class DocumentUrls
{
    public const string HostName = "doc.mdglint.example";

    private const string UncPrefix = "UNC";

    /// <summary>Returns the folder's URL with a trailing slash, or null if the path can't be expressed.</summary>
    public static string? ToFolderUrl(string folder)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(folder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var segments = new List<string>();
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) || fullPath.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return null;
        }

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            segments.Add(UncPrefix);
            segments.AddRange(fullPath[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries));
            if (segments.Count < 3)
            {
                return null;
            }
        }
        else
        {
            // Any other full path starts with a drive letter and a colon.
            segments.Add(char.ToUpperInvariant(fullPath[0]).ToString());
            segments.AddRange(fullPath[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries));
        }

        return $"https://{HostName}/" + string.Join('/', segments.Select(Uri.EscapeDataString)) + "/";
    }

    /// <summary>Maps a document-host URL back to a full local path.</summary>
    public static bool TryGetPath(Uri uri, out string path)
    {
        path = string.Empty;
        if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, HostName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // AbsolutePath is already normalised: "." and ".." segments are resolved and can't climb
        // above the first segment.
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToList();
        if (segments.Count == 0 || segments.Any(s => s is "." or ".." || s.IndexOfAny(['/', '\\', ':']) >= 0))
        {
            return false;
        }

        string root;
        int rest;
        if (segments[0] == UncPrefix)
        {
            if (segments.Count < 3)
            {
                return false;
            }

            root = $@"\\{segments[1]}\{segments[2]}\";
            rest = 3;
        }
        else if (segments[0].Length == 1 && char.IsAsciiLetter(segments[0][0]))
        {
            root = $@"{char.ToUpperInvariant(segments[0][0])}:\";
            rest = 1;
        }
        else
        {
            return false;
        }

        try
        {
            return TryAcceptUnder(Path.GetFullPath(Path.Combine([root, .. segments.Skip(rest)])), root, out path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// The last check that a URL can't name a path outside its root. The segment checks above
    /// already rule that out; this stays in case a future change lets one through.
    /// </summary>
    internal static bool TryAcceptUnder(string candidate, string root, out string path)
    {
        path = candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? candidate : string.Empty;
        return path.Length > 0;
    }
}
