using System.IO;
using System.Text;
using MDGlint.Core.Models;

namespace MDGlint.Desktop.Helpers;

/// <summary>
/// The styles inlined into a file saved with Export ▸ HTML: the theme's github-markdown-css and
/// highlight.js stylesheets, each after its license, then MDGlint's page layout (export.css).
/// </summary>
public static class ExportStyles
{
    public static string For(string assetsFolder, ThemeKind theme)
    {
        var name = theme.ToStorageValue();
        var markdown = Path.Combine(assetsFolder, "vendor", "github-markdown-css");
        var code = Path.Combine(assetsFolder, "vendor", "highlight.js");

        var styles = new StringBuilder();
        AppendLicensed(styles, "github-markdown-css", Path.Combine(markdown, "LICENSE"), Path.Combine(markdown, $"github-markdown-{name}.css"));
        AppendLicensed(styles, "highlight.js", Path.Combine(code, "LICENSE"),
            Path.Combine(code, theme == ThemeKind.Dark ? "github-dark.min.css" : "github.min.css"));
        styles.Append(File.ReadAllText(Path.Combine(assetsFolder, "export.css")));
        return styles.ToString();
    }

    private static void AppendLicensed(StringBuilder styles, string name, string licenseFile, string styleFile)
    {
        // A comment can't hold "*/"; no license text has one.
        var license = File.ReadAllText(licenseFile).Replace("*/", "* /", StringComparison.Ordinal).Trim();
        styles.Append($"/*\n  {name}\n\n{license}\n*/\n");
        styles.Append(File.ReadAllText(styleFile).Trim());
        styles.Append("\n\n");
    }
}
