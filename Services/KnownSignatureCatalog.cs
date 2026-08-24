namespace UE4SSInstaller.Services;

public sealed class KnownSignaturePack
{
    public required string DisplayName { get; init; }
    public required string Owner { get; init; }
    public required string Repo { get; init; }
    public string? SteamAppId { get; init; }
    public string[] NameContains { get; init; } = [];
    public string[] FolderNames { get; init; } = [];
    public int? EngineMajorVersion { get; init; }
    public int? EngineMinorVersion { get; init; }
    public bool HasEngineVersionOverride => EngineMajorVersion is not null && EngineMinorVersion is not null;
    /// <summary>
    /// When set, Install UE4SS downloads this Git SHA from experimental-latest instead of the newest zip.
    /// </summary>
    public string? PinnedUe4ssGitSha { get; init; }
    public bool HasPinnedUe4ss => !string.IsNullOrWhiteSpace(PinnedUe4ssGitSha);
    public IniPatch[] IniPatches { get; init; } = [];
}

public sealed record IniPatch(string Section, string Key, string Value);

/// <summary>
/// Game-specific UE4SS signature packs. Matched by Steam app id, then name, then folder.
/// Each pack's latest GitHub release zip is extracted into <c>UE4SS_Signatures</c> after UE4SS install.
/// </summary>
public static class KnownSignatureCatalog
{
    public static readonly IReadOnlyList<KnownSignaturePack> Packs =
    [
        new KnownSignaturePack
        {
            DisplayName = "Mortal Shell II signatures",
            Owner = "mattdavida",
            Repo = "MortalShell2-UE4SS-Fix",
            SteamAppId = "2584270",
            NameContains = ["Mortal Shell II", "Mortal Shell 2"],
            FolderNames = ["MortalShell2", "MortalShellII", "Mortal Shell II"],
            // experimental after d7e7826d (#1387 FSoftObjectPath / #1389 SDK generator) AVs on tarstone DTs.
            PinnedUe4ssGitSha = "d7e7826d"
        },
        new KnownSignaturePack
        {
            DisplayName = "Witchfire signatures",
            Owner = "mattdavida",
            Repo = "Witchfire-ue4ss-fix",
            SteamAppId = "3156770",
            NameContains = ["Witchfire"],
            FolderNames = ["Witchfire"],
            EngineMajorVersion = 4,
            EngineMinorVersion = 27
        },
        new KnownSignaturePack
        {
            DisplayName = "Wuchang: Fallen Feathers signatures",
            Owner = "mattdavida",
            Repo = "Wuchang-UE4SS-Fix",
            SteamAppId = "2277560",
            NameContains = ["Wuchang", "Fallen Feathers"],
            FolderNames = ["Wuchang Fallen Feathers", "WUCHANG Fallen Feathers"],
            IniPatches =
            [
                new("Hooks", "HookInitGameState", "0")
            ]
        }
    ];

    public static KnownSignaturePack? Find(DetectedGame game)
        => Find(game.AppId, game.Name, game.InstallPath, game.Win64Path);

    public static KnownSignaturePack? Find(string? appId, string? name, string? installPath, string? win64Path)
    {
        foreach (var pack in Packs)
        {
            if (Matches(pack, appId, name, installPath, win64Path))
                return pack;
        }

        return null;
    }

    private static bool Matches(
        KnownSignaturePack pack,
        string? appId,
        string? name,
        string? installPath,
        string? win64Path)
    {
        if (!string.IsNullOrEmpty(pack.SteamAppId)
            && !string.IsNullOrEmpty(appId)
            && string.Equals(pack.SteamAppId, appId, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var token in pack.NameContains)
            {
                if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return FolderMatches(pack, installPath) || FolderMatches(pack, win64Path);
    }

    private static bool FolderMatches(KnownSignaturePack pack, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || pack.FolderNames.Length == 0)
            return false;

        foreach (var segment in path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var folder in pack.FolderNames)
            {
                if (segment.Equals(folder, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
