namespace UE4SSInstaller.Services;

public sealed class KnownSignaturePack
{
    public required string DisplayName { get; init; }
    public string Owner { get; init; } = "";
    public string Repo { get; init; } = "";
    public string[] SteamAppIds { get; init; } = [];
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
    public Ue4ssReleaseSource? Ue4ssSource { get; init; }
    public bool HasCustomUe4ssSource => Ue4ssSource is not null;
    public bool HasSignaturePack => !string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repo);
    public string? BadgeText { get; init; }
    public string? InstallHint { get; init; }
    public IniPatch[] IniPatches { get; init; } = [];

    public string SupportBadge
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BadgeText))
                return BadgeText;
            if (HasSignaturePack)
                return "Signatures";
            return DisplayName;
        }
    }
}

public sealed record IniPatch(string Section, string Key, string Value);

/// <summary>
/// Game-specific UE4SS handling. Matched by Steam app id, then name, then folder.
/// Signature packs extract <c>.lua</c> files into <c>UE4SS_Signatures</c> after UE4SS install.
/// Palworld instead downloads the rolling community zip from <see cref="Ue4ssReleaseSource.Palworld"/>.
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
            SteamAppIds = ["2584270"],
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
            SteamAppIds = ["3156770"],
            NameContains = ["Witchfire"],
            FolderNames = ["Witchfire"],
            EngineMajorVersion = 4,
            EngineMinorVersion = 27
        },
        new KnownSignaturePack
        {
            DisplayName = "Fatal Claw signatures",
            Owner = "mattdavida",
            Repo = "FatalClaw-UE4SS-Fix",
            SteamAppIds = ["2827750"],
            NameContains = ["Fatal Claw"],
            FolderNames = ["FatalClaw", "Fatal Claw"],
            EngineMajorVersion = 4,
            EngineMinorVersion = 27
        },
        new KnownSignaturePack
        {
            DisplayName = "Wuchang: Fallen Feathers signatures",
            Owner = "mattdavida",
            Repo = "Wuchang-UE4SS-Fix",
            SteamAppIds = ["2277560"],
            NameContains = ["Wuchang", "Fallen Feathers"],
            FolderNames = ["Wuchang Fallen Feathers", "WUCHANG Fallen Feathers"],
            IniPatches =
            [
                new("Hooks", "HookInitGameState", "0")
            ]
        },
        // Palworld docs (pwmodding.wiki) recommend Okaetsu/RE-UE4SS experimental-palworld.
        // That tag is rolling and kept in sync with Workshop; we fetch it on every install.
        // Signatures are not used (they broke Palworld after the 2024 launch period).
        new KnownSignaturePack
        {
            DisplayName = "Palworld UE4SS",
            SteamAppIds = ["1623730", "2394010"],
            NameContains = ["Palworld"],
            FolderNames = ["Palworld", "Palworld Dedicated Server"],
            Ue4ssSource = Ue4ssReleaseSource.Palworld,
            BadgeText = "Palworld zip",
            InstallHint = "Uses the Palworld community zip (experimental-palworld). Disable Workshop UE4SS first or the game will crash."
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
        if (!string.IsNullOrEmpty(appId))
        {
            foreach (var id in pack.SteamAppIds)
            {
                if (string.Equals(id, appId, StringComparison.Ordinal))
                    return true;
            }
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
