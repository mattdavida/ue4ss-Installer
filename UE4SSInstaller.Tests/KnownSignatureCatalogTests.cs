using UE4SSInstaller.Services;

namespace UE4SSInstaller.Tests;

public sealed class KnownSignatureCatalogTests
{
    [Fact]
    public void Matches_Mortal_Shell_II_by_app_id()
    {
        var pack = KnownSignatureCatalog.Find("2584270", "Something Else", null, null);
        Assert.Equal("MortalShell2-UE4SS-Fix", pack?.Repo);
        Assert.Equal("d7e7826d", pack?.PinnedUe4ssGitSha);
        Assert.True(pack?.HasPinnedUe4ss);
        Assert.True(pack?.HasSignaturePack);
        Assert.Null(pack?.Ue4ssSource);
    }

    [Fact]
    public void Matches_Witchfire_by_name_and_sets_engine_override()
    {
        var pack = KnownSignatureCatalog.Find(null, "Witchfire", null, null);
        Assert.Equal("Witchfire-ue4ss-fix", pack?.Repo);
        Assert.True(pack?.HasEngineVersionOverride);
        Assert.False(pack?.HasPinnedUe4ss);
        Assert.Equal(4, pack?.EngineMajorVersion);
        Assert.Equal(27, pack?.EngineMinorVersion);
    }

    [Fact]
    public void Matches_Fatal_Claw_by_app_id_and_sets_engine_override()
    {
        var pack = KnownSignatureCatalog.Find("2827750", "Something Else", null, null);
        Assert.Equal("FatalClaw-UE4SS-Fix", pack?.Repo);
        Assert.True(pack?.HasEngineVersionOverride);
        Assert.False(pack?.HasPinnedUe4ss);
        Assert.Equal(4, pack?.EngineMajorVersion);
        Assert.Equal(27, pack?.EngineMinorVersion);
    }

    [Fact]
    public void Matches_Fatal_Claw_by_folder_name()
    {
        var pack = KnownSignatureCatalog.Find(null, null, @"D:\SteamLibrary\steamapps\common\Fatal Claw", null);
        Assert.Equal("FatalClaw-UE4SS-Fix", pack?.Repo);
    }

    [Fact]
    public void Matches_Wuchang_by_app_id_and_includes_hook_patches()
    {
        var pack = KnownSignatureCatalog.Find("2277560", "Something Else", null, null);
        Assert.Equal("Wuchang-UE4SS-Fix", pack?.Repo);
        Assert.False(pack?.HasPinnedUe4ss);
        Assert.Equal(new IniPatch("Hooks", "HookInitGameState", "0"), Assert.Single(pack!.IniPatches));
    }

    [Fact]
    public void Unknown_games_have_no_pack()
    {
        Assert.Null(KnownSignatureCatalog.Find("1", "Asterigos: Curse of the Stars", @"D:\Steam\Asterigos", null));
    }

    [Fact]
    public void Matches_Palworld_by_app_id_and_uses_the_community_zip()
    {
        var pack = KnownSignatureCatalog.Find("1623730", "Something Else", null, null);
        Assert.Equal("Palworld UE4SS", pack?.DisplayName);
        Assert.False(pack?.HasSignaturePack);
        Assert.True(pack?.HasCustomUe4ssSource);
        Assert.Equal(Ue4ssReleaseSource.Palworld, pack?.Ue4ssSource);
        Assert.Equal("Palworld zip", pack?.SupportBadge);
        Assert.Contains("Workshop", pack!.InstallHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Matches_Palworld_dedicated_server_by_app_id()
    {
        var pack = KnownSignatureCatalog.Find("2394010", "Dedicated", null, null);
        Assert.Equal(Ue4ssReleaseSource.Palworld, pack?.Ue4ssSource);
    }

    [Fact]
    public void Matches_Palworld_by_folder_name()
    {
        var pack = KnownSignatureCatalog.Find(null, null, @"D:\SteamLibrary\steamapps\common\Palworld", null);
        Assert.Equal(Ue4ssReleaseSource.Palworld, pack?.Ue4ssSource);
    }
}
