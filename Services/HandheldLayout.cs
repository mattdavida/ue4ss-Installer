using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace UE4SSInstaller.Services;

/// <summary>
/// The default 500×700 window keeps the desktop layout.
/// Maximize or fullscreen ("expanded") uses the handheld full-screen flow.
/// Screens smaller than an 11" laptop start maximized so that flow is the default.
/// </summary>
public static class HandheldLayout
{
    public const double SmallScreenInches = 11;

    public readonly record struct Decision(bool IsHandheld, string Reason);

    public static Decision Detect(WindowState windowState, string? layoutEnv = null)
    {
        layoutEnv ??= Environment.GetEnvironmentVariable("UE4SS_INSTALLER_LAYOUT");
        if (string.Equals(layoutEnv, "handheld", StringComparison.OrdinalIgnoreCase))
            return new Decision(true, "forced (UE4SS_INSTALLER_LAYOUT)");
        if (string.Equals(layoutEnv, "desktop", StringComparison.OrdinalIgnoreCase))
            return new Decision(false, "forced (UE4SS_INSTALLER_LAYOUT)");

        if (windowState is WindowState.Maximized or WindowState.FullScreen)
            return new Decision(true, "expanded");

        return new Decision(false, "windowed");
    }

    public static bool ShouldForceExpanded(double? diagonalInches, string? layoutEnv = null)
    {
        layoutEnv ??= Environment.GetEnvironmentVariable("UE4SS_INSTALLER_LAYOUT");
        if (string.Equals(layoutEnv, "desktop", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(layoutEnv, "handheld", StringComparison.OrdinalIgnoreCase))
            return true;

        return diagonalInches is > 0 and < SmallScreenInches;
    }

    public static double? TryMeasurePrimaryDiagonalInches()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var hdc = GetDC(0);
        if (hdc == 0)
            return null;

        try
        {
            var widthMm = GetDeviceCaps(hdc, HorzSize);
            var heightMm = GetDeviceCaps(hdc, VertSize);
            if (widthMm <= 0 || heightMm <= 0)
                return null;

            var inches = Math.Sqrt((widthMm * widthMm) + (heightMm * heightMm)) / 25.4;
            return inches is >= 5 and < 40 ? inches : null;
        }
        finally
        {
            ReleaseDC(0, hdc);
        }
    }

    private const int HorzSize = 4;
    private const int VertSize = 6;

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(nint hdc, int index);
}
