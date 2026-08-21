using System.Text.Json;
using System.Text.Json.Serialization;

namespace UE4SSInstaller.Services;

public sealed class InstalledMod
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    public ModPackageKind Kind { get; set; }

    /// <summary>Paths relative to <c>Binaries/Win64</c>.</summary>
    public List<string> Files { get; set; } = [];
}

public sealed class ModsManifest
{
    public const string FileName = ".ue4ss-installer-mods.json";

    public List<InstalledMod> Mods { get; set; } = [];
}

public static class ModTracker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<InstalledMod> List(string win64Path)
        => Load(win64Path).Mods;

    public static void SaveMod(string win64Path, InstalledMod mod)
    {
        var manifest = Load(win64Path);
        var existing = manifest.Mods.FindIndex(m =>
            string.Equals(m.Name, mod.Name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            mod.Id = manifest.Mods[existing].Id;
            manifest.Mods[existing] = mod;
        }
        else
        {
            manifest.Mods.Add(mod);
        }

        manifest.Mods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Save(win64Path, manifest);
    }

    public static InstalledMod? Remove(string win64Path, string modId)
    {
        var manifest = Load(win64Path);
        var index = manifest.Mods.FindIndex(m => m.Id == modId);
        if (index < 0)
            return null;

        var removed = manifest.Mods[index];
        manifest.Mods.RemoveAt(index);
        Save(win64Path, manifest);
        return removed;
    }

    public static ModsManifest Load(string win64Path)
    {
        var merged = new ModsManifest();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in ManifestPaths(win64Path))
        {
            if (!File.Exists(path))
                continue;

            ModsManifest? parsed = null;
            try
            {
                parsed = JsonSerializer.Deserialize<ModsManifest>(File.ReadAllText(path), JsonOptions);
            }
            catch
            {
                // Treat a corrupt list as empty; the next save overwrites it.
            }

            if (parsed is null)
                continue;

            foreach (var mod in parsed.Mods)
            {
                if (string.IsNullOrWhiteSpace(mod.Name) || !seen.Add(mod.Name))
                    continue;

                merged.Mods.Add(mod);
            }
        }

        merged.Mods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return merged;
    }

    private static void Save(string win64Path, ModsManifest manifest)
    {
        var path = PreferredPath(win64Path);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));

        foreach (var extra in ManifestPaths(win64Path))
        {
            if (string.Equals(extra, path, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (File.Exists(extra))
                    File.Delete(extra);
            }
            catch
            {
                // Best-effort consolidation.
            }
        }
    }

    public static string PreferredPath(string win64Path)
    {
        var ue4ss = Path.Combine(win64Path, "ue4ss");
        if (Directory.Exists(ue4ss))
            return Path.Combine(ue4ss, ModsManifest.FileName);

        return Path.Combine(win64Path, ModsManifest.FileName);
    }

    private static IEnumerable<string> ManifestPaths(string win64Path)
    {
        yield return Path.Combine(win64Path, "ue4ss", ModsManifest.FileName);
        yield return Path.Combine(win64Path, ModsManifest.FileName);
    }
}
