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
    /// Removes UE4SS: deletes <c>ue4ss/</c> (mods and signatures included) and Win64-root
    /// proxy DLLs such as <c>dwmapi.dll</c>. Extra root files from a managed manifest are
    /// deleted too.
    /// </summary>
    public static void UninstallUe4ss(string win64Path)
    {
        var manifest = InstallTracker.TryLoad(win64Path);

        if (manifest is not null)
        {
            foreach (var relative in manifest.Files)
            {
                var norm = NormalizeRelative(relative);
                if (norm.StartsWith("ue4ss/", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(norm, "ue4ss", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var dest = SafeCombine(win64Path, norm);
                if (dest is null)
                    continue;

                TryDeleteFile(dest);
            }
        }

        foreach (var name in new[] { "dwmapi.dll", "UE4SS.dll" })
            TryDeleteFile(Path.Combine(win64Path, name));

        TryDeleteFile(Path.Combine(win64Path, InstallerManifest.FileName));

        var ue4ssDir = Path.Combine(win64Path, "ue4ss");
        if (Directory.Exists(ue4ssDir))
            Directory.Delete(ue4ssDir, recursive: true);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            throw new IOException($"Could not delete {path}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Installs a user zip. Full UE4SS packs (<c>dwmapi.dll</c> + <c>ue4ss/</c>, optionally
    /// inside one wrapper folder) and <c>ue4ss/</c> overlays (signatures, extra mods) extract
    /// into Win64. Everything else extracts into the Mods folder.
    /// Not recorded in the UE4SS manifest, so channel switches will not delete these files.
    /// Recorded in <c>.ue4ss-installer-mods.json</c> for per-mod uninstall.
    /// </summary>
    public static ModInstallResult InstallMod(string zipPath, string win64Path)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var layout = InspectModZip(archive);

        var destination = layout.Kind == ModPackageKind.GameDirectory
            ? win64Path
            : ResolveModsDirectory(win64Path);

        Directory.CreateDirectory(destination);
        var extracted = ExtractMapped(archive, destination, layout.StripPrefix);
        var relativeToWin64 = extracted
            .Select(relative => RelativizeToWin64(win64Path, destination, relative))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var name = Path.GetFileNameWithoutExtension(zipPath);
        if (string.IsNullOrWhiteSpace(name))
            name = "Mod";

        var previous = ModTracker.Load(win64Path).Mods
            .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        if (previous is not null)
        {
            var keep = new HashSet<string>(relativeToWin64, StringComparer.OrdinalIgnoreCase);
            keep.UnionWith(Ue4ssOwnedFiles(win64Path));
            RemoveOrphans(win64Path, previous.Files, keep);
        }

        ModTracker.SaveMod(win64Path, new InstalledMod
        {
            Name = name,
            Kind = layout.Kind,
            Files = relativeToWin64
        });

        return new ModInstallResult(layout.Kind, destination, name, relativeToWin64);
    }

    public static void UninstallMod(string win64Path, string modId)
    {
        var mod = ModTracker.List(win64Path).FirstOrDefault(m => m.Id == modId)
                  ?? throw new InvalidOperationException("That mod is not in the installer list.");

        var keep = Ue4ssOwnedFiles(win64Path);
        RemoveOrphans(win64Path, mod.Files, keep);

        foreach (var relative in mod.Files)
        {
            if (keep.Contains(NormalizeRelative(relative)))
                continue;

            var full = SafeCombine(win64Path, relative);
            if (full is not null && File.Exists(full))
            {
                throw new IOException(
                    $"Could not delete {full}. Close the game and try again.");
            }
        }

        ModTracker.Remove(win64Path, modId);
    }

    private static HashSet<string> Ue4ssOwnedFiles(string win64Path)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifest = InstallTracker.TryLoad(win64Path);
        if (manifest is not null)
        {
            foreach (var file in manifest.Files)
                owned.Add(NormalizeRelative(file));
        }

        owned.Add(NormalizeRelative(Path.Combine("ue4ss", InstallerManifest.FileName)));
        owned.Add(NormalizeRelative(Path.Combine("ue4ss", ModsManifest.FileName)));
        owned.Add(InstallerManifest.FileName);
        owned.Add(ModsManifest.FileName);
        return owned;
    }

    private static string RelativizeToWin64(string win64Path, string destination, string relativeToDest)
    {
        var destFile = SafeCombine(destination, relativeToDest);
        if (destFile is null)
            return NormalizeRelative(relativeToDest);

        var winFull = Path.GetFullPath(win64Path);
        var prefix = winFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return NormalizeRelative(relativeToDest);

        return NormalizeRelative(destFile[prefix.Length..]);
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
    {
        var fileName = Path.GetFileName(relative);
        return string.Equals(fileName, InstallerManifest.FileName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(fileName, ModsManifest.FileName, StringComparison.OrdinalIgnoreCase);
    }

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

    private static List<string> ExtractMapped(ZipArchive archive, string destination, string? stripPrefix)
    {
        var extracted = new List<string>();
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
            extracted.Add(NormalizeRelative(relative));
        }

        return extracted;
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

public sealed record ModInstallResult(
    ModPackageKind Kind,
    string Destination,
    string Name,
    IReadOnlyList<string> Files);

internal sealed record ModZipLayout(ModPackageKind Kind, string? StripPrefix);
