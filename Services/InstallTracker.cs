using System.Text.Json;
using System.Text.Json.Serialization;

namespace UE4SSInstaller.Services;

public enum InstallKind
{
    None,
    Managed,
    Unmanaged
}

public sealed class InstallState
{
    public required InstallKind Kind { get; init; }
    public Ue4ssChannel? Channel { get; init; }

    public string StatusText => Kind switch
    {
        InstallKind.Managed => $"Currently {FormatChannel(Channel)} (managed by this installer).",
        InstallKind.Unmanaged => "UE4SS is present but was not installed by this app. Install once with the installer before switching Release/zDev, or leftovers may remain.",
        _ => "UE4SS is not installed."
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
