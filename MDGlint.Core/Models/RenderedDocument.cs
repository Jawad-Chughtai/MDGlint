namespace MDGlint.Core.Models;

/// <summary>
/// The result of rendering a file: an HTML fragment for the preview page, and the folder that
/// relative links and images resolve against (null for message pages). <see cref="OffersOpen"/>
/// is true only for the page shown when no file is open, where the shell adds its own
/// <b>Open…</b> button. <see cref="Title"/> is the file's name, which becomes the page title (and
/// so the title of a printout or PDF); null for message pages.
/// </summary>
public sealed record RenderedDocument(string Html, string? BaseDirectory, bool OffersOpen = false, string? Title = null);
