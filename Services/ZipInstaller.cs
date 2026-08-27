using System.IO.Compression;
using System.Text.RegularExpressions;

namespace UE4SSInstaller.Services;

public static class ZipInstaller
{
    private static readonly Regex DuplicateDownloadSuffix = new(
        @"\s*\(\d+\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TimestampToken = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}[-:]\d{2}(?:[-:]\d{2})?Z$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DottedVersionToken = new(
        @"^\d+\.\d+(?:\.\d+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IntegerToken = new(
        @"^\d+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Extracts the UE4SS zip into <c>Binaries/Win64</c>, then removes files from the previous
    /// installer-owned manifest that are not in this zip (zDev → Release leftover cleanup).
    /// A single wrapper folder (for example Palworld's <c>UE4SS-Palworld_zDev/</c>) is stripped
    /// so <c>dwmapi.dll</c> and <c>ue4ss/</c> land in Win64. Same-channel updates keep an
    /// existing <c>UE4SS-settings.ini</c>. Channel switches overwrite it.
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
        var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var win64Full = Path.GetFullPath(win64Path);

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
                var parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent))
                    parents.Add(parent);
            }
        }

        foreach (var name in new[] { "dwmapi.dll", "UE4SS.dll" })
            TryDeleteFile(Path.Combine(win64Path, name));

        TryDeleteFile(Path.Combine(win64Path, InstallerManifest.FileName));
        TryDeleteFile(Path.Combine(win64Path, ModsManifest.FileName));

        var ue4ssDir = Path.Combine(win64Path, "ue4ss");
        if (Directory.Exists(ue4ssDir))
            Directory.Delete(ue4ssDir, recursive: true);

        foreach (var dir in parents.OrderByDescending(p => p.Length))
            TryDeleteEmptyAncestors(dir, win64Full);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            ClearReadOnly(path);
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
    /// Identity is the inner UE4SS mod folder (not the download filename), so a Nexus zip
    /// with a unique hash is still the same mod. A matching tracked install is removed first
    /// (failing if the game still has those files open), then the new zip is copied.
    /// Not recorded in the UE4SS manifest, so channel switches will not delete these files.
    /// Recorded in <c>.ue4ss-installer-mods.json</c> for per-mod uninstall.
    /// </summary>
    public static ModInstallResult InstallMod(string zipPath, string win64Path)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var layout = InspectModZip(archive);
        var incoming = RelativesToWin64(win64Path, layout.Kind, ListMappedFiles(archive, layout.StripPrefix));
        var name = InferModName(incoming, zipPath);

        var previous = FindPreviousMods(win64Path, name, incoming);
        var reinstalled = previous.Count > 0;
        var previousId = previous.FirstOrDefault(m =>
                             string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))?.Id
                         ?? previous.FirstOrDefault()?.Id;
        foreach (var mod in previous)
            UninstallMod(win64Path, mod.Id);

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

        var record = new InstalledMod
        {
            Name = name,
            Kind = layout.Kind,
            Files = relativeToWin64
        };
        if (previousId is not null)
            record.Id = previousId;

        ModTracker.SaveMod(win64Path, record);
        return new ModInstallResult(layout.Kind, destination, name, relativeToWin64, reinstalled);
    }

    internal static ModInstallPreview PreviewModInstall(string zipPath, string win64Path)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var layout = InspectModZip(archive);
        var incoming = RelativesToWin64(win64Path, layout.Kind, ListMappedFiles(archive, layout.StripPrefix));
        var name = InferModName(incoming, zipPath);
        return new ModInstallPreview(name, layout.Kind, FindPreviousMods(win64Path, name, incoming).Count > 0);
    }

    internal static bool WouldReinstall(string zipPath, string win64Path)
        => PreviewModInstall(zipPath, win64Path).WouldReinstall;

    internal static string FormatModInstallStatus(ModInstallResult result)
    {
        var where = result.Kind == ModPackageKind.GameDirectory
            ? "the game folder (UE4SS pack / overlay)"
            : "the Mods folder";
        var verb = result.Reinstalled ? "Reinstalled" : "Installed";
        return $"{verb} {result.Name} into {where}.";
    }

    internal static string InferModName(IEnumerable<string> win64Relatives, string zipPath)
    {
        var plugins = PluginFolders(win64Relatives);
        if (plugins.Count == 1)
        {
            var folder = plugins.First();
            var nested = NestedModFolders(win64Relatives, folder);
            if (nested.Count == 1 && IsDownloadNoiseName(folder) && !IsDownloadNoiseName(nested.First()))
                return CleanDisplayName(nested.First());

            return CleanDisplayName(folder);
        }

        return CleanZipStem(zipPath);
    }

    internal static string CleanZipStem(string zipPath)
        => CleanDisplayName(Path.GetFileNameWithoutExtension(zipPath));

    internal static string CleanDisplayName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Mod";

        var name = DuplicateDownloadSuffix.Replace(raw.Trim(), "").Trim();
        var strippedMetadata = false;
        while (true)
        {
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                break;

            if (!IsDisposableZipToken(parts[^1])
                && !IsArchiveIdAfterTimestamp(parts))
                break;

            strippedMetadata = true;
            name = string.Join(' ', parts[..^1]);
        }

        if (strippedMetadata)
        {
            while (true)
            {
                var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !IntegerToken.IsMatch(parts[^1]))
                    break;

                name = string.Join(' ', parts[..^1]);
            }
        }

        return string.IsNullOrWhiteSpace(name) ? "Mod" : name;
    }

    internal static IReadOnlyList<InstalledMod> FindPreviousMods(
        string win64Path,
        string name,
        IReadOnlyList<string> incomingFiles)
    {
        var incomingSet = new HashSet<string>(
            incomingFiles.Select(NormalizeRelative),
            StringComparer.OrdinalIgnoreCase);
        var incomingPlugins = PluginFolders(incomingSet);
        var keep = Ue4ssOwnedFiles(win64Path);
        var matches = new List<InstalledMod>();

        foreach (var mod in ModTracker.List(win64Path))
        {
            if (string.Equals(mod.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(mod);
                continue;
            }

            var modPlugins = PluginFolders(mod.Files);
            if (incomingPlugins.Count > 0 && modPlugins.Overlaps(incomingPlugins))
            {
                matches.Add(mod);
                continue;
            }

            if (mod.Files.Select(NormalizeRelative).Any(file =>
                    incomingSet.Contains(file) && !keep.Contains(file)))
            {
                matches.Add(mod);
            }
        }

        return matches;
    }

    internal static HashSet<string> PluginFolders(IEnumerable<string> win64Relatives)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in win64Relatives)
        {
            var path = NormalizeRelative(relative);
            if (TryFolderAfterPrefix(path, "ue4ss/Mods/", out var folder)
                || TryFolderAfterPrefix(path, "Mods/", out folder))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }

    /// <summary>
    /// ConfigManager writes <c>config.json</c> next to <c>Scripts/</c> at runtime. That file is
    /// not in the zip, so uninstall/reinstall must drop it or stale keys survive an update.
    /// </summary>
    internal static IReadOnlyList<string> ConfigManagerSidecarFiles(
        string win64Path,
        IEnumerable<string> modFiles)
    {
        var files = modFiles.Select(NormalizeRelative).ToList();
        var sidecars = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in PluginFolders(files))
        {
            foreach (var root in new[] { "ue4ss/Mods/" + folder, "Mods/" + folder })
            {
                var scriptsPrefix = NormalizeRelative(root + "/Scripts") + "/";
                var scriptsDirRel = NormalizeRelative(root + "/Scripts");
                var hasScriptsTracked = files.Any(file =>
                    file.StartsWith(scriptsPrefix, StringComparison.OrdinalIgnoreCase)
                    || file.Equals(scriptsDirRel, StringComparison.OrdinalIgnoreCase));
                var scriptsDir = SafeCombine(win64Path, scriptsDirRel);
                var hasScriptsOnDisk = scriptsDir is not null && Directory.Exists(scriptsDir);
                if (!hasScriptsTracked && !hasScriptsOnDisk)
                    continue;

                var configRel = NormalizeRelative(root + "/config.json");
                if (!seen.Add(configRel))
                    continue;

                sidecars.Add(configRel);
            }
        }

        return sidecars;
    }

    internal static HashSet<string> NestedModFolders(IEnumerable<string> win64Relatives, string pluginFolder)
    {
        var children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefixes = new[]
        {
            "ue4ss/Mods/" + pluginFolder.Trim('/') + "/",
            "Mods/" + pluginFolder.Trim('/') + "/"
        };

        foreach (var relative in win64Relatives)
        {
            var path = NormalizeRelative(relative);
            foreach (var prefix in prefixes)
            {
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var child = FirstSegment(path[prefix.Length..]);
                if (child.Length == 0 || IsLikelyFileName(child) || IsBuiltinModSubfolder(child))
                    continue;

                children.Add(child);
            }
        }

        return children;
    }

    private static bool TryFolderAfterPrefix(string relative, string prefix, out string folder)
    {
        folder = "";
        if (!relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        folder = FirstSegment(relative[prefix.Length..]);
        if (folder.Length == 0)
            return false;

        var afterFolder = prefix.Length + folder.Length;
        return relative.Length > afterFolder && relative[afterFolder] == '/';
    }

    private static bool IsLikelyFileName(string segment)
        => segment.Contains('.');

    private static bool IsBuiltinModSubfolder(string segment)
        => segment.Equals("Scripts", StringComparison.OrdinalIgnoreCase)
           || segment.Equals("Binaries", StringComparison.OrdinalIgnoreCase)
           || segment.Equals("Content", StringComparison.OrdinalIgnoreCase)
           || segment.Equals("Config", StringComparison.OrdinalIgnoreCase);

    private static bool IsDownloadNoiseName(string name)
        => !string.Equals(name, CleanDisplayName(name), StringComparison.OrdinalIgnoreCase);

    private static bool IsDisposableZipToken(string token)
        => TimestampToken.IsMatch(token)
           || DottedVersionToken.IsMatch(token)
           || LooksLikeNexusHash(token);

    private static bool IsArchiveIdAfterTimestamp(string[] parts)
    {
        if (parts.Length < 2 || !TimestampToken.IsMatch(parts[^2]))
            return false;

        var token = parts[^1];
        if (token.Length is < 8 or > 12)
            return false;

        foreach (var c in token)
        {
            if (!char.IsAsciiLetterOrDigit(c))
                return false;
        }

        return token.Any(char.IsAsciiLetter);
    }

    private static bool LooksLikeNexusHash(string token)
    {
        if (token.Length is < 8 or > 12)
            return false;

        var hasLetter = false;
        var hasDigit = false;
        foreach (var c in token)
        {
            if (char.IsAsciiLetter(c))
                hasLetter = true;
            else if (char.IsAsciiDigit(c))
                hasDigit = true;
            else
                return false;
        }

        if (!hasLetter)
            return false;
        if (hasDigit)
            return true;

        return HasIrregularCasing(token);
    }

    private static bool HasIrregularCasing(string token)
    {
        var upper = 0;
        var lower = 0;
        foreach (var c in token)
        {
            if (char.IsUpper(c))
                upper++;
            else if (char.IsLower(c))
                lower++;
        }

        if (upper == 0 || lower == 0)
            return false;
        if (upper == 1 && char.IsUpper(token[0]))
            return false;

        if (!char.IsUpper(token[0]))
            return true;

        for (var i = 1; i < token.Length; i++)
        {
            if (char.IsUpper(token[i]) && char.IsUpper(token[i - 1]))
                return true;
        }

        return false;
    }

    private static List<string> ListMappedFiles(ZipArchive archive, string? stripPrefix)
    {
        var files = new List<string>();
        foreach (var entry in archive.Entries)
        {
            if (!TryMapEntry(entry, stripPrefix, out var relative, out var isDirectory) || isDirectory)
                continue;

            files.Add(relative);
        }

        return files;
    }

    private static List<string> RelativesToWin64(
        string win64Path,
        ModPackageKind kind,
        IEnumerable<string> mappedRelatives)
    {
        var destination = kind == ModPackageKind.GameDirectory
            ? win64Path
            : ResolveModsDirectory(win64Path);
        return mappedRelatives
            .Select(relative => RelativizeToWin64(win64Path, destination, relative))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryMapEntry(
        ZipArchiveEntry entry,
        string? stripPrefix,
        out string relative,
        out bool isDirectory)
    {
        relative = "";
        isDirectory = false;

        var normalized = NormalizeRelative(entry.FullName);
        if (normalized.Length == 0 || IsJunkPath(normalized))
            return false;

        normalized = StripPrefix(normalized, stripPrefix);
        if (normalized.Length == 0 || IsManifestFile(normalized))
            return false;

        isDirectory = string.IsNullOrEmpty(entry.Name)
                      || entry.FullName.EndsWith('/')
                      || entry.FullName.EndsWith('\\');
        relative = NormalizeRelative(normalized);
        return true;
    }

    public static void UninstallMod(string win64Path, string modId)
    {
        var mod = ModTracker.List(win64Path).FirstOrDefault(m => m.Id == modId)
                  ?? throw new InvalidOperationException("That mod is not in the installer list.");

        var keep = Ue4ssOwnedFiles(win64Path);
        var toRemove = mod.Files
            .Concat(ConfigManagerSidecarFiles(win64Path, mod.Files))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        ThrowIfModFilesInUse(win64Path, toRemove, keep);
        RemoveOrphans(win64Path, toRemove, keep);

        foreach (var relative in toRemove)
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
        var layout = InspectModZip(archive);
        var stripPrefix = layout.Kind == ModPackageKind.GameDirectory ? layout.StripPrefix : null;

        foreach (var entry in archive.Entries)
        {
            var relative = NormalizeRelative(entry.FullName);
            if (relative.Length == 0)
                continue;

            relative = StripPrefix(relative, stripPrefix);
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

    private static void ThrowIfModFilesInUse(string win64Path, IEnumerable<string> files, HashSet<string> keep)
    {
        foreach (var relative in files)
        {
            var norm = NormalizeRelative(relative);
            if (keep.Contains(norm) || IsManifestFile(norm))
                continue;

            var full = SafeCombine(win64Path, relative);
            if (full is null || !File.Exists(full))
                continue;

            try
            {
                using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"Could not delete {full}. Close the game and try again.", ex);
            }
        }
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch
        {
            // Delete/extract will surface the real error.
        }
    }

    private static void RemoveOrphans(string win64Path, IEnumerable<string> previousFiles, HashSet<string> keep)
    {
        var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in previousFiles)
        {
            var norm = NormalizeRelative(relative);
            if (keep.Contains(norm) || IsManifestFile(norm))
                continue;

            var full = SafeCombine(win64Path, relative);
            if (full is null)
                continue;

            try
            {
                if (!File.Exists(full))
                    continue;

                ClearReadOnly(full);
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

    internal static ModPackageKind PeekModZipKind(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return InspectModZip(archive).Kind;
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
            if (!TryMapEntry(entry, stripPrefix, out var relative, out var isDirectory))
                continue;

            var dest = SafeCombine(destination, relative);
            if (dest is null)
                continue;

            if (isDirectory)
            {
                Directory.CreateDirectory(dest);
                continue;
            }

            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            if (File.Exists(dest))
                ClearReadOnly(dest);

            entry.ExtractToFile(dest, overwrite: true);
            extracted.Add(relative);
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
            || prefix.Equals("Mods", StringComparison.OrdinalIgnoreCase))
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

public sealed record ModInstallPreview(string Name, ModPackageKind Kind, bool WouldReinstall);

public sealed record ModInstallResult(
    ModPackageKind Kind,
    string Destination,
    string Name,
    IReadOnlyList<string> Files,
    bool Reinstalled);

internal sealed record ModZipLayout(ModPackageKind Kind, string? StripPrefix);
