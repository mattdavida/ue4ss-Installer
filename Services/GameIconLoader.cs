using System.Runtime.Versioning;
using Avalonia.Media.Imaging;
using DrawingIcon = System.Drawing.Icon;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;

namespace UE4SSInstaller.Services;

public static class GameIconLoader
{
    public static Bitmap? Load(string? exePath, string? steamArtworkPath)
    {
        var fromSteam = TryLoadFile(steamArtworkPath);
        if (fromSteam is not null)
            return fromSteam;

        if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(exePath))
            return LoadFromExe(exePath);

        return null;
    }

    public static string? FindSteamArtwork(string steamPath, string appId)
    {
        if (string.IsNullOrWhiteSpace(steamPath) || string.IsNullOrWhiteSpace(appId))
            return null;

        var cacheRoot = Path.Combine(steamPath, "appcache", "librarycache");
        var appDir = Path.Combine(cacheRoot, appId);
        if (Directory.Exists(appDir))
        {
            var ranked = RankArtwork(SafeEnumerate(appDir, "*.jpg")
                .Concat(SafeEnumerate(appDir, "*.png")));
            if (ranked is not null)
                return ranked;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(cacheRoot, $"{appId}_icon.jpg"),
                     Path.Combine(cacheRoot, $"{appId}.jpg"),
                     Path.Combine(cacheRoot, appId, "icon.jpg"),
                     Path.Combine(cacheRoot, appId, "icon.png")
                 })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static Bitmap? LoadFromExe(string exePath)
    {
        try
        {
            using var icon = DrawingIcon.ExtractAssociatedIcon(exePath);
            if (icon is null)
                return null;

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, DrawingImageFormat.Png);
            stream.Position = 0;
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryLoadFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    private static string? RankArtwork(IEnumerable<string> files)
    {
        string? icon = null;
        string? other = null;

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (name.Contains("icon", StringComparison.OrdinalIgnoreCase))
            {
                icon = file;
                break;
            }

            if (name.StartsWith("library", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("header", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("logo", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("hero", StringComparison.OrdinalIgnoreCase))
                continue;

            other ??= file;
        }

        return icon ?? other;
    }

    private static IEnumerable<string> SafeEnumerate(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern);
        }
        catch
        {
            return [];
        }
    }
}
