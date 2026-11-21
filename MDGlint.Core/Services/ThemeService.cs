using MDGlint.Core.Models;

namespace MDGlint.Core.Services;

public sealed class ThemeService
{
    public ThemeService(string? theme)
    {
        Theme = ThemeKindExtensions.FromStorageValue(theme);
    }

    public ThemeKind Theme { get; private set; }

    public bool IsDark => Theme == ThemeKind.Dark;

    public void Toggle()
    {
        Theme = IsDark ? ThemeKind.Light : ThemeKind.Dark;
    }
}
