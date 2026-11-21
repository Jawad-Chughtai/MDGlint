using System.Windows;
using MDGlint.Core.Models;

namespace MDGlint.Desktop.Helpers;

/// <summary>A window's position and size, in device-independent pixels.</summary>
public readonly record struct WindowBounds(double Left, double Top, double Width, double Height);

/// <summary>
/// Where the window opens and what it remembers when it closes: maximized by default, and
/// otherwise at its last normal position and size, kept on the screens the computer has now.
/// </summary>
public static class WindowPlacement
{
    /// <summary>How much of the window must be on screen for the saved position to be used.</summary>
    internal const double MinimumVisible = 100;

    /// <summary>
    /// The saved normal bounds, fitted to <paramref name="screen"/> (the virtual screen) and the
    /// window's minimum size; null when none were saved or they're no longer on screen (a monitor
    /// was removed), so the window opens centred at its default size.
    /// </summary>
    public static WindowBounds? Restore(AppSettings settings, WindowBounds screen, double minWidth, double minHeight)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings is not { WindowLeft: { } left, WindowTop: { } top, WindowWidth: { } width, WindowHeight: { } height }
            || !IsUsable(left, top, width, height))
        {
            return null;
        }

        // Never bigger than the screen, never smaller than the window allows.
        width = Math.Max(Math.Min(width, screen.Width), minWidth);
        height = Math.Max(Math.Min(height, screen.Height), minHeight);

        var screenRight = screen.Left + screen.Width;
        var screenBottom = screen.Top + screen.Height;
        var visibleWidth = Math.Min(left + width, screenRight) - Math.Max(left, screen.Left);
        var visibleHeight = Math.Min(top + height, screenBottom) - Math.Max(top, screen.Top);
        if (visibleWidth < MinimumVisible || visibleHeight < MinimumVisible)
        {
            return null;
        }

        // Partly off screen: move it back so the title bar can be reached.
        left = Math.Max(Math.Min(left, screenRight - width), screen.Left);
        top = Math.Max(Math.Min(top, screenBottom - height), screen.Top);
        return new WindowBounds(left, top, width, height);
    }

    /// <summary>
    /// Records the window as it closes. <paramref name="restoreBounds"/> is its normal position
    /// and size (WPF's <c>RestoreBounds</c>, also right while maximized or minimized). Closing
    /// while minimized keeps the previous maximized choice.
    /// </summary>
    public static void Remember(AppSettings settings, Rect restoreBounds, WindowState state)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (state != WindowState.Minimized)
        {
            settings.WindowMaximized = state == WindowState.Maximized;
        }

        if (!restoreBounds.IsEmpty && IsUsable(restoreBounds.Left, restoreBounds.Top, restoreBounds.Width, restoreBounds.Height))
        {
            settings.WindowLeft = restoreBounds.Left;
            settings.WindowTop = restoreBounds.Top;
            settings.WindowWidth = restoreBounds.Width;
            settings.WindowHeight = restoreBounds.Height;
        }
    }

    private static bool IsUsable(double left, double top, double width, double height) =>
        double.IsFinite(left) && double.IsFinite(top) && double.IsFinite(width) && double.IsFinite(height)
        && width > 0 && height > 0;
}
