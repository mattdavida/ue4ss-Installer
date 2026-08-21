using System.IO.Compression;

namespace UE4SSInstaller.Services;

public static class ZipInstaller
{
    /// <summary>
    /// Extracts the UE4SS zip into <c>Binaries/Win64</c>, then removes files from the previous
    /// installer-owned manifest that are not in this zip (zDev → Release leftover cleanup).
    /// Same-channel updates keep an existing <c>UE4SS-settings.ini</c>. Channel switches overwrite it.
    /// </summary>
    public static void InstallUe4ss(string zipPath, string win64Path, Ue4ssChannel channel)
    {
        Directory.CreateDirectory(win64Path);

        var previous = InstallTracker.TryLoad(win64Path);
        var preserveSettings = previous is not null
                               && previous.Channel == channel
                               && SettingsIniExists(win64Path);

        var extracted = ExtractZip(zipPath, win64Path, preserveSettings);
        var extractedSet = new HashSet<string>(extracted, StringComparer.OrdinalIgnoreCase);

        if (previous is not null)
            RemoveOrphans(win64Path, previous.Files, extractedSet);

        // Keep tracking leftovers we could not delete so the next install retries.
        foreach (var leftover in previous?.Files ?? [])
        {
            if (extractedSet.Contains(leftover))
                continue;

            var full = SafeCombine(win64Path, leftover);
            if (full is not null && File.Exists(full))
                extractedSet.Add(NormalizeRelative(leftover));
        }

        InstallTracker.Save(win64Path, new InstallerManifest
        {
            Channel = channel,
            Files = extractedSet.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
        });
    }

    /// <summary>
    /// Installs a user zip. Full UE4SS packs (<c>dwmapi.dll</c> + <c>ue4ss/</c>, optionally
    /// inside one wrapper folder) and <c>ue4ss/</c> overlays (signatures, extra mods) extract
    /// into Win64. Everything else extracts into the Mods folder.
    /// Not recorded in the UE4SS manifest, so channel switches will not delete these files.
    /// </summary>
    public static ModInstallResult InstallMod(string zipPath, string win64Path)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var layout = InspectModZip(archive);

        var destination = layout.Kind == ModPackageKind.GameDirectory
            ? win64Path
            : ResolveModsDirectory(win64Path);

        Directory.CreateDirectory(destination);
        ExtractMapped(archive, destination, layout.StripPrefix);

        return new ModInstallResult(layout.Kind, destination);
    }

    /// <summary>
    /// Extracts <c>.lua</c> files into <c>ue4ss/UE4SS_Signatures</c>.
    /// Creates that folder when it is missing (Release). Does not copy if <c>ue4ss/</c> is absent.
    /// Not recorded in the UE4SS manifest.
    /// </summary>
    public static string InstallSignaturePack(string zipPath, string win64Path)
    {
        if (!TryGetSignaturesDirectory(win64Path, out var destination))
        {
            throw new InvalidOperationException(
                "UE4SS did not create a ue4ss folder, so signatures were not copied.");
        }

        Directory.CreateDirectory(destination);

        var count = 0;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)
                || !entry.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileName = Path.GetFileName(entry.Name);
            if (string.IsNullOrEmpty(fileName)
                || fileName is "." or ".."
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                continue;
            }

            var dest = Path.Combine(destination, fileName);
            entry.ExtractToFile(dest, overwrite: true);
            count++;
        }

        if (count == 0)
            throw new InvalidOperationException("The signature zip did not contain any .lua files.");

        return destination;
    }

    public static bool TryGetSignaturesDirectory(string win64Path, out string destination)
    {
        var ue4ssDir = Path.Combine(win64Path, "ue4ss");
        if (!Directory.Exists(ue4ssDir))
        {
            destination = string.Empty;
            return false;
        }

        destination = Path.Combine(ue4ssDir, "UE4SS_Signatures");
        return true;
    }

    public static string ResolveModsDirectory(string win64Path)
    {
        var ue4ssDir = Path.Combine(win64Path, "ue4ss");
        if (Directory.Exists(ue4ssDir))
            return Path.Combine(ue4ssDir, "Mods");

        return Path.Combine(win64Path, "Mods");
    }

    private static bool SettingsIniExists(string win64Path)
        => File.Exists(Path.Combine(win64Path, "ue4ss", "UE4SS-settings.ini"))
           || File.Exists(Path.Combine(win64Path, "UE4SS-settings.ini"));

    private static List<string> ExtractZip(string zipPath, string win64Path, bool preserveSettings)
    {
        var extracted = new List<string>();
        using var archive = ZipFile.OpenRead(zipPath);

        foreach (var entry in archive.Entries)
        {
            var relative = NormalizeRelative(entry.FullName);
            if (relative.Length == 0 || IsJunkPath(relative))
                continue;

            var dest = SafeCombine(win64Path, relative);
            if (dest is null)
                continue;

            var isDirectory = string.IsNullOrEmpty(entry.Name)
                              || entry.FullName.EndsWith('/')
                              || entry.FullName.EndsWith('\\');
            if (isDirectory)
            {
                Directory.CreateDirectory(dest);
                continue;
            }

            if (preserveSettings && IsSettingsIni(relative) && File.Exists(dest))
            {
                extracted.Add(relative);
                continue;
            }

            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            entry.ExtractToFile(dest, overwrite: true);
            extracted.Add(relative);
        }

        return extracted;
    }

    private static void RemoveOrphans(string win64Path, IEnumerable<string> previousFiles, HashSet<string> keep)
    {
        var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in previousFiles)
        {
            if (keep.Contains(relative) || IsManifestFile(relative))
                continue;

            var full = SafeCombine(win64Path, relative);
            if (full is null)
                continue;

            try
            {
                if (File.Exists(full))
                    File.Delete(full);
            }
            catch
            {
                continue;
            }

            var parent = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(parent))
                parents.Add(parent);
        }

        var win64Full = Path.GetFullPath(win64Path);
        foreach (var dir in parents.OrderByDescending(p => p.Length))
        {
            TryDeleteEmptyAncestors(dir, win64Full);
        }
    }

    private static void TryDeleteEmptyAncestors(string directory, string win64Full)
    {
        var current = directory;
        while (!string.IsNullOrEmpty(current)
               && current.StartsWith(win64Full, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(current, win64Full, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(current) && !Directory.EnumerateFileSystemEntries(current).Any())
                    Directory.Delete(current);
                else
                    break;
            }
            catch
            {
                break;
            }

            current = Path.GetDirectoryName(current) ?? "";
        }
    }

    private static bool IsSettingsIni(string relative)
        => relative.EndsWith("UE4SS-settings.ini", StringComparison.OrdinalIgnoreCase);

    private static bool IsManifestFile(string relative)
        => relative.EndsWith(InstallerManifest.FileName, StringComparison.OrdinalIgnoreCase)
           || string.Equals(Path.GetFileName(relative), InstallerManifest.FileName, StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeRelative(string relative)
        => relative.Replace('\\', '/').Trim('/');

    internal static string? SafeCombine(string root, string relative)
    {
        var rootFull = Path.GetFullPath(root);
        var combined = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(combined, rootFull, StringComparison.OrdinalIgnoreCase))
            return null;

        return combined;
    }

    internal static ModZipLayout InspectModZip(ZipArchive archive)
    {
        var relatives = archive.Entries
            .Select(entry => NormalizeRelative(entry.FullName))
            .Where(relative => relative.Length > 0 && !IsJunkPath(relative))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var strip = DetectWrapperPrefix(relatives);
        var effective = StripPrefix(relatives, strip);

        if (LooksLikeGameDirectoryPackage(effective))
        {
            // OPTION A / overlay: dwmapi + ue4ss, or ue4ss/ (signatures, extra mods) onto Win64.
            return new ModZipLayout(ModPackageKind.GameDirectory, strip);
        }

        var modsPrefix = CombinePrefix(strip, "Mods");
        if (HasRootFolder(effective, "Mods"))
            return new ModZipLayout(ModPackageKind.ModsFolder, modsPrefix);

        return new ModZipLayout(ModPackageKind.ModsFolder, strip);
    }

    private static void ExtractMapped(ZipArchive archive, string destination, string? stripPrefix)
    {
        foreach (var entry in archive.Entries)
        {
            var relative = NormalizeRelative(entry.FullName);
            if (relative.Length == 0 || IsJunkPath(relative))
                continue;

            relative = StripPrefix(relative, stripPrefix);
            if (relative.Length == 0)
                continue;

            if (IsManifestFile(relative))
                continue;

            var dest = SafeCombine(destination, relative);
            if (dest is null)
                continue;

            var isDirectory = string.IsNullOrEmpty(entry.Name)
                              || entry.FullName.EndsWith('/')
                              || entry.FullName.EndsWith('\\');
            if (isDirectory)
            {
                Directory.CreateDirectory(dest);
                continue;
            }

            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    private static bool LooksLikeGameDirectoryPackage(IReadOnlyList<string> relatives)
    {
        var hasDwmapi = relatives.Any(path =>
            path.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase));
        var hasUe4ss = HasRootFolder(relatives, "ue4ss")
                       || relatives.Any(path => path.Equals("UE4SS.dll", StringComparison.OrdinalIgnoreCase));

        return hasUe4ss && (hasDwmapi || HasRootFolder(relatives, "ue4ss"));
    }

    private static string? DetectWrapperPrefix(IReadOnlyList<string> relatives)
    {
        var tops = relatives
            .Select(FirstSegment)
            .Where(segment => segment.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tops.Count != 1)
            return null;

        var prefix = tops[0];
        if (prefix.Equals("ue4ss", StringComparison.OrdinalIgnoreCase)
            || prefix.Equals("Mods", StringComparison.OrdinalIgnoreCase)
            || prefix.Contains('.'))
            return null;

        if (!relatives.Any(path => path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)))
            return null;

        var stripped = StripPrefix(relatives, prefix);
        if (LooksLikeGameDirectoryPackage(stripped) || HasRootFolder(stripped, "Mods"))
            return prefix;

        return null;
    }

    private static bool HasRootFolder(IEnumerable<string> relatives, string folder)
        => relatives.Any(path =>
            path.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));

    private static string FirstSegment(string relative)
    {
        var slash = relative.IndexOf('/');
        return slash < 0 ? relative : relative[..slash];
    }

    private static List<string> StripPrefix(IEnumerable<string> relatives, string? prefix)
        => relatives.Select(path => StripPrefix(path, prefix))
            .Where(path => path.Length > 0)
            .ToList();

    private static string StripPrefix(string relative, string? prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return relative;

        if (relative.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var head = prefix.Trim('/') + "/";
        return relative.StartsWith(head, StringComparison.OrdinalIgnoreCase)
            ? relative[head.Length..]
            : relative;
    }

    private static string? CombinePrefix(string? first, string second)
        => string.IsNullOrEmpty(first) ? second : $"{first.Trim('/')}/{second}";

    private static bool IsJunkPath(string relative)
    {
        var first = FirstSegment(relative);
        return first.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)
               || first.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase)
               || relative.EndsWith(".DS_Store", StringComparison.OrdinalIgnoreCase)
               || relative.EndsWith("Thumbs.db", StringComparison.OrdinalIgnoreCase);
    }
}

public enum ModPackageKind
{
    GameDirectory,
    ModsFolder
}

public sealed record ModInstallResult(ModPackageKind Kind, string Destination);

internal sealed record ModZipLayout(ModPackageKind Kind, string? StripPrefix);
