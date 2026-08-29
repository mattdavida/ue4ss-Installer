using System.Text.Json;
using System.Text.Json.Serialization;

namespace UE4SSInstaller.Services;

public enum InstallKind
{
    None,
    Managed,
    CustomMod,
    Unmanaged
}

public sealed class InstallState
{
    public required InstallKind Kind { get; init; }
    public Ue4ssChannel? Channel { get; init; }
    public string? CustomModName { get; init; }

    public string StatusText => Kind switch
    {
        InstallKind.Managed => $"Currently {FormatChannel(Channel)} (managed by this installer).",
        InstallKind.CustomMod => $"Currently a custom UE4SS (installed via {CustomModName}).",
        InstallKind.Unmanaged => "UE4SS is present but was not installed by this app. Install once with the installer before switching Release/zDev, or leftovers may remain.",
        _ => "UE4SS is not installed."
    };

    /// <summary>Short game-list badge. Custom packs use <c>via {mod}</c>.</summary>
    public string? GameBadge => Kind switch
    {
        InstallKind.Managed => FormatChannel(Channel),
        InstallKind.CustomMod => string.IsNullOrWhiteSpace(CustomModName) ? "Custom" : $"via {CustomModName}",
        _ => null
    };

    public string? GameBadgeTip => Kind switch
    {
        InstallKind.Managed => $"UE4SS {FormatChannel(Channel)} (managed by this installer).",
        InstallKind.CustomMod => string.IsNullOrWhiteSpace(CustomModName)
            ? "Installed via a custom UE4SS mod."
            : $"Installed via mod: {CustomModName}",
        _ => null
    };

    private static string FormatChannel(Ue4ssChannel? channel)
        => channel == Ue4ssChannel.ZDev ? "zDev" : "Release";
}

public sealed class InstallerManifest
{
    public const string FileName = ".ue4ss-installer.json";

    public Ue4ssChannel Channel { get; set; }

    public List<string> Files { get; set; } = [];
}

public static class InstallTracker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static InstallState Detect(string win64Path)
    {
        var manifest = TryLoad(win64Path);
        if (manifest is not null)
            return new InstallState { Kind = InstallKind.Managed, Channel = manifest.Channel };

        var custom = ModTracker.FindUe4ssProvider(win64Path);
        if (custom is not null)
        {
            return new InstallState
            {
                Kind = InstallKind.CustomMod,
                CustomModName = custom.DisplayName
            };
        }

        if (LooksInstalled(win64Path))
            return new InstallState { Kind = InstallKind.Unmanaged };

        return new InstallState { Kind = InstallKind.None };
    }

    public static InstallerManifest? TryLoad(string win64Path)
    {
        foreach (var path in ManifestPaths(win64Path))
        {
            if (!File.Exists(path))
                continue;

            try
            {
                var json = File.ReadAllText(path);
                var manifest = JsonSerializer.Deserialize<InstallerManifest>(json, JsonOptions);
                if (manifest is not null)
                    return manifest;
            }
            catch
            {
                // Ignore a corrupt marker and treat the install as unmanaged.
            }
        }

        return null;
    }

    public static void Save(string win64Path, InstallerManifest manifest)
    {
        var path = PreferredManifestPath(win64Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    public static string PreferredManifestPath(string win64Path)
        => Path.Combine(win64Path, "ue4ss", InstallerManifest.FileName);

    private static IEnumerable<string> ManifestPaths(string win64Path)
    {
        yield return PreferredManifestPath(win64Path);
        yield return Path.Combine(win64Path, InstallerManifest.FileName);
    }

    private static bool LooksInstalled(string win64Path)
        => File.Exists(Path.Combine(win64Path, "dwmapi.dll"))
           || File.Exists(Path.Combine(win64Path, "UE4SS.dll"))
           || File.Exists(Path.Combine(win64Path, "ue4ss", "UE4SS.dll"))
           || Directory.Exists(Path.Combine(win64Path, "ue4ss"));
}
