using System.IO.Compression;
using UE4SSInstaller.Services;

namespace UE4SSInstaller.Tests;

public sealed class InspectModZipTests
{
    [Fact]
    public void Treats_dwmapi_plus_ue4ss_as_a_game_directory_pack()
    {
        using var temp = new TempDir();
        var zip = TestZip.Create(temp.Path,
            ("dwmapi.dll", "proxy"),
            ("ue4ss/UE4SS.dll", "core"));

        using var archive = ZipFile.OpenRead(zip);
        var layout = ZipInstaller.InspectModZip(archive);

        Assert.Equal(ModPackageKind.GameDirectory, layout.Kind);
        Assert.Null(layout.StripPrefix);
    }

    [Fact]
    public void Unwraps_a_single_wrapper_folder()
    {
        using var temp = new TempDir();
        var zip = TestZip.Create(temp.Path,
            ("UE4SS_v3/dwmapi.dll", "proxy"),
            ("UE4SS_v3/ue4ss/UE4SS.dll", "core"));

        using var archive = ZipFile.OpenRead(zip);
        var layout = ZipInstaller.InspectModZip(archive);

        Assert.Equal(ModPackageKind.GameDirectory, layout.Kind);
        Assert.Equal("UE4SS_v3", layout.StripPrefix);
    }

    [Fact]
    public void Treats_a_ue4ss_overlay_as_game_directory()
    {
        using var temp = new TempDir();
        var zip = TestZip.Create(temp.Path,
            ("ue4ss/UE4SS_Signatures/ConsoleManager.lua", "sig"));

        using var archive = ZipFile.OpenRead(zip);
        var layout = ZipInstaller.InspectModZip(archive);

        Assert.Equal(ModPackageKind.GameDirectory, layout.Kind);
    }

    [Fact]
    public void Strips_a_leading_Mods_folder()
    {
        using var temp = new TempDir();
        var zip = TestZip.Create(temp.Path,
            ("Mods/MyMod/Scripts/main.lua", "print('hi')"));

        using var archive = ZipFile.OpenRead(zip);
        var layout = ZipInstaller.InspectModZip(archive);

        Assert.Equal(ModPackageKind.ModsFolder, layout.Kind);
        Assert.Equal("Mods", layout.StripPrefix);
    }

    [Fact]
    public void Treats_loose_lua_as_a_mods_folder_zip()
    {
        using var temp = new TempDir();
        var zip = TestZip.Create(temp.Path,
            ("MyMod/Scripts/main.lua", "print('hi')"));

        using var archive = ZipFile.OpenRead(zip);
        var layout = ZipInstaller.InspectModZip(archive);

        Assert.Equal(ModPackageKind.ModsFolder, layout.Kind);
        Assert.Null(layout.StripPrefix);
    }

    [Fact]
    public void Peek_from_path_matches_inspect()
    {
        using var temp = new TempDir();
        var pack = TestZip.Create(temp.Path,
            ("dwmapi.dll", "proxy"),
            ("ue4ss/UE4SS.dll", "core"));
        var loose = TestZip.Create(temp.Path,
            ("MyMod/Scripts/main.lua", "print('hi')"));

        Assert.Equal(ModPackageKind.GameDirectory, ZipInstaller.PeekModZipKind(pack));
        Assert.Equal(ModPackageKind.ModsFolder, ZipInstaller.PeekModZipKind(loose));
    }
}
