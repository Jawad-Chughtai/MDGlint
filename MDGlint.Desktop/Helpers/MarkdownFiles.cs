using System.IO;

namespace MDGlint.Desktop.Helpers;

public static class MarkdownFiles
{
    public static bool IsMarkdownFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase);
    }
}
