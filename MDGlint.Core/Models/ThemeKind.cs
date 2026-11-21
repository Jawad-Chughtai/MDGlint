namespace MDGlint.Core.Models;

public enum ThemeKind
{
    Light,
    Dark
}

public static class ThemeKindExtensions
{
    public static ThemeKind FromStorageValue(string? value)
    {
        return string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase)
            ? ThemeKind.Dark
            : ThemeKind.Light;
    }

    public static string ToStorageValue(this ThemeKind theme)
    {
        return theme == ThemeKind.Dark ? "dark" : "light";
    }
}
