using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Ganss.Xss;

namespace MDGlint.Core.Services;

/// <summary>
/// Cleans the HTML produced from a Markdown file before it reaches the preview. Markdown may
/// contain raw HTML, and the file is untrusted, so only an allow-list of passive elements and
/// attributes is kept: scripts, event handlers, frames, forms, styles sheets and other active
/// content are removed. The preview's Content Security Policy blocks script as a second layer.
/// </summary>
public sealed partial class HtmlContentSanitizer
{
    // Tags HtmlSanitizer allows by default that have no place in a document preview: they submit
    // data or take input.
    private static readonly string[] RemovedTags =
    [
        "button", "datalist", "fieldset", "form", "keygen", "legend", "menuitem", "optgroup",
        "option", "output", "select", "textarea"
    ];

    // Local media is served by the desktop app; <picture> is common in READMEs for light and
    // dark images.
    private static readonly string[] AddedTags = ["audio", "picture", "source", "video"];

    private static readonly string[] RemovedAttributes =
    [
        "accept", "accept-charset", "accesskey", "action", "autocomplete", "autosave", "challenge",
        "enctype", "keytype", "method", "novalidate", "pattern", "placeholder", "prompt",
        "radiogroup", "required", "target"
    ];

    // id: heading anchors and footnotes. class: code languages, task lists, footnotes.
    private static readonly string[] AddedAttributes =
    [
        "class", "controls", "id", "loop", "muted", "playsinline", "poster", "sizes", "srcset"
    ];

    private readonly HtmlSanitizer _sanitizer = new();

    public HtmlContentSanitizer()
    {
        foreach (var tag in RemovedTags)
        {
            _sanitizer.AllowedTags.Remove(tag);
        }

        _sanitizer.AllowedTags.UnionWith(AddedTags);

        foreach (var attribute in RemovedAttributes)
        {
            _sanitizer.AllowedAttributes.Remove(attribute);
        }

        _sanitizer.AllowedAttributes.UnionWith(AddedAttributes);

        // "file" for links and images given as file: URLs; "data" is narrowed to inline images
        // in OnFilterUrl.
        _sanitizer.AllowedSchemes.UnionWith(["mailto", "file", "data"]);

        _sanitizer.FilterUrl += OnFilterUrl;
        _sanitizer.PostProcessNode += OnPostProcessNode;
    }

    public string Sanitize(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        return _sanitizer.Sanitize(html);
    }

    private static void OnFilterUrl(object? sender, FilterUrlEventArgs e)
    {
        // "C:\docs\a.png" reads as a URL with scheme "c", so the sanitizer drops it. It is a local
        // path, which the preview rewrites to its local-file address, so keep it. Markdown links
        // and images arrive with the backslash encoded ("C:%5Cdocs%5Ca.png"); app.js decodes it.
        if (e.SanitizedUrl is null && DrivePath().IsMatch(e.OriginalUrl))
        {
            e.SanitizedUrl = e.OriginalUrl;
            return;
        }

        // data: URLs only as images, never as links or other sources.
        if (e.SanitizedUrl is not null
            && e.SanitizedUrl.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            && !(e.Tag.LocalName == "img" && DataImage().IsMatch(e.SanitizedUrl)))
        {
            e.SanitizedUrl = null;
        }
    }

    private static void OnPostProcessNode(object? sender, PostProcessNodeEventArgs e)
    {
        // Inputs only appear in task lists: a disabled checkbox. Anything else is removed.
        if (e.Node is IElement { LocalName: "input" } input)
        {
            if (!string.Equals(input.GetAttribute("type"), "checkbox", StringComparison.OrdinalIgnoreCase))
            {
                input.Remove();
                return;
            }

            input.SetAttribute("disabled", "disabled");
            input.RemoveAttribute("name");
            input.RemoveAttribute("value");
        }
    }

    [GeneratedRegex(@"^\s*[A-Za-z]:(?:[\\/]|%5[Cc])")]
    private static partial Regex DrivePath();

    [GeneratedRegex(@"^\s*data:image/[a-z0-9.+-]+[;,]", RegexOptions.IgnoreCase)]
    private static partial Regex DataImage();
}
