using System.IO;

namespace MDGlint.Desktop.Helpers;

/// <summary>Where the Export dialogs start: beside the open file, with its name.</summary>
public static class ExportFiles
{
    /// <summary>The open file's name with <paramref name="extension"/> in place of its own.</summary>
    public static string SuggestedName(string markdownPath, string extension) =>
        Path.GetFileNameWithoutExtension(markdownPath) + extension;

    /// <summary>The open file's folder.</summary>
    public static string? SuggestedFolder(string markdownPath) =>
        Path.GetDirectoryName(Path.GetFullPath(markdownPath));
}
