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
}
