using UE4SSInstaller.Services;

namespace UE4SSInstaller.Tests;

public sealed class CustomUe4ssFlowTests
{
    [Fact]
    public void MortalShell2_lua_zips_do_not_reinstall_or_overwrite_the_custom_pack()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);
        var (pack, handheld, desktop) = CreateMortalShell2Zips(temp);

        var packResult = ZipInstaller.InstallMod(pack, win64);
        Assert.False(packResult.Reinstalled);
        Assert.Equal("MortalShell2-ue4ss", packResult.Name);
        Assert.Equal(ModPackageKind.GameDirectory, packResult.Kind);

        var afterPack = InstallTracker.Detect(win64);
        Assert.Equal(InstallKind.CustomMod, afterPack.Kind);
        Assert.Equal("MortalShell2-ue4ss", afterPack.CustomModName);
        Assert.Equal("via MortalShell2-ue4ss", afterPack.GameBadge);
        Assert.Equal("MortalShell2-ue4ss", Assert.Single(ModTracker.List(win64)).Name);
        Assert.True(Assert.Single(ModTracker.List(win64)).ProvidesUe4ss);

        var ue4ssDll = Path.Combine(win64, "ue4ss", "UE4SS.dll");
        var packDll = File.ReadAllBytes(ue4ssDll);
        Assert.False(ZipInstaller.WouldReinstall(handheld, win64));

        var handheldPreview = ZipInstaller.PreviewModInstall(handheld, win64);
        Assert.Equal("MortalShell2Mod", handheldPreview.Name);
        Assert.Equal(ModPackageKind.ModsFolder, handheldPreview.Kind);
        Assert.False(handheldPreview.WouldReinstall);

        var handheldResult = ZipInstaller.InstallMod(handheld, win64);
        Assert.False(handheldResult.Reinstalled);
        Assert.Equal("MortalShell2Mod", handheldResult.Name);
        Assert.Equal(packDll, File.ReadAllBytes(ue4ssDll));
        Assert.True(File.Exists(Path.Combine(win64, "dwmapi.dll")));
        Assert.Equal("sig", File.ReadAllText(Path.Combine(win64, "ue4ss", "UE4SS_Signatures", "FName.lua")));
        Assert.Equal("console", File.ReadAllText(Path.Combine(win64, "ue4ss", "Mods", "ConsoleEnablerMod", "Scripts", "main.lua")));
        Assert.Equal("handheld", File.ReadAllText(LuaMain(win64)));
        Assert.True(File.Exists(HandheldOnly(win64)));

        var afterHandheld = ModTracker.List(win64);
        Assert.Equal(
            ["MortalShell2-ue4ss", "MortalShell2Mod"],
            afterHandheld.Select(mod => mod.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList());
        var detected = InstallTracker.Detect(win64);
        Assert.Equal(InstallKind.CustomMod, detected.Kind);
        Assert.Equal("MortalShell2-ue4ss", detected.CustomModName);
        Assert.Single(afterHandheld, mod => mod.ProvidesUe4ss);

        Assert.True(ZipInstaller.WouldReinstall(desktop, win64));
        var desktopResult = ZipInstaller.InstallMod(desktop, win64);
        Assert.True(desktopResult.Reinstalled);
        Assert.Equal("MortalShell2Mod", desktopResult.Name);
        Assert.Equal(packDll, File.ReadAllBytes(ue4ssDll));
        Assert.Equal("desktop", File.ReadAllText(LuaMain(win64)));
        Assert.False(File.Exists(HandheldOnly(win64)));
        Assert.Equal("desktop-shared", File.ReadAllText(Path.Combine(win64, "ue4ss", "Mods", "shared", "ConfigManager", "ConfigManager.lua")));

        var afterDesktop = ModTracker.List(win64);
        Assert.Equal(
            ["MortalShell2-ue4ss", "MortalShell2Mod"],
            afterDesktop.Select(mod => mod.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList());
        Assert.Single(afterDesktop, mod => mod.ProvidesUe4ss);
        Assert.Equal(InstallKind.CustomMod, InstallTracker.Detect(win64).Kind);
    }

    [Theory]
    [InlineData("ue4ss")]
    [InlineData("mod")]
    public void Either_uninstall_path_wipes_the_custom_pack_and_every_tracked_mod(string path)
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);
        var (pack, handheld, _) = CreateMortalShell2Zips(temp);

        ZipInstaller.InstallMod(pack, win64);
        ZipInstaller.InstallMod(handheld, win64);
        var provider = Assert.Single(ModTracker.List(win64), mod => mod.ProvidesUe4ss);
        var before = MainWindow.GetInstalledModsState(win64);
        Assert.Equal(2, before.Mods.Count);
        Assert.NotNull(before.Selected);

        if (path == "ue4ss")
            ZipInstaller.UninstallUe4ss(win64);
        else
            ZipInstaller.UninstallMod(win64, provider.Id);

        AssertWiped(win64, before.Selected!.Id);
        Assert.False(File.Exists(Path.Combine(win64, "README.txt")));
    }

    [Fact]
    public void Uninstall_ue4ss_clears_read_only_files_inside_ue4ss()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);
        var (pack, _, _) = CreateMortalShell2Zips(temp);
        ZipInstaller.InstallMod(pack, win64);

        var locked = Path.Combine(win64, "ue4ss", "UE4SS.dll");
        File.SetAttributes(locked, File.GetAttributes(locked) | FileAttributes.ReadOnly);
        var readme = Path.Combine(win64, "README.txt");
        File.SetAttributes(readme, File.GetAttributes(readme) | FileAttributes.ReadOnly);

        ZipInstaller.UninstallUe4ss(win64);

        AssertWiped(win64);
        Assert.False(File.Exists(readme));
    }

    [Fact]
    public void Uninstall_mod_confirm_copy_warns_that_a_provider_wipes_everything()
    {
        var pack = new InstalledMod
        {
            Name = "MortalShell2-ue4ss",
            Kind = ModPackageKind.GameDirectory,
            Files = ["dwmapi.dll", "ue4ss/UE4SS.dll"]
        };
        var lua = new InstalledMod
        {
            Name = "MortalShell2Mod",
            Kind = ModPackageKind.ModsFolder,
            Files = ["ue4ss/Mods/MortalShell2Mod/Scripts/main.lua"]
        };

        Assert.Contains("every mod this app installed", MainWindow.DescribeUninstallMod(pack), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UE4SS", MainWindow.DescribeUninstallMod(pack), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "This deletes the files this app installed for that mod. UE4SS itself is left alone.",
            MainWindow.DescribeUninstallMod(lua));
    }

    [Fact]
    public void GetInstalledModsState_is_empty_after_a_custom_pack_wipe()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);
        var (pack, handheld, _) = CreateMortalShell2Zips(temp);
        ZipInstaller.InstallMod(pack, win64);
        ZipInstaller.InstallMod(handheld, win64);

        var before = MainWindow.GetInstalledModsState(win64);
        Assert.NotEmpty(before.Mods);
        Assert.NotNull(before.Selected);

        ZipInstaller.UninstallUe4ss(win64);

        var after = MainWindow.GetInstalledModsState(win64, before.Selected!.Id);
        Assert.Empty(after.Mods);
        Assert.Null(after.Selected);
        Assert.Null(InstallTracker.Detect(win64).GameBadge);
    }

    private static (string Pack, string Handheld, string Desktop) CreateMortalShell2Zips(TempDir temp)
    {
        var zips = temp.Combine("zips");
        var pack = TestZip.CreateNamed(zips, "MortalShell2-ue4ss.zip",
            ("dwmapi.dll", "proxy"),
            ("README.txt", "pack notes"),
            ("ue4ss/UE4SS.dll", "pack-core"),
            ("ue4ss/UE4SS-settings.ini", "GuiEnabled = 1"),
            ("ue4ss/UE4SS_Signatures/FName.lua", "sig"),
            ("ue4ss/Mods/MortalShell2Mod/Scripts/main.lua", "pack"),
            ("ue4ss/Mods/ConsoleEnablerMod/Scripts/main.lua", "console"),
            ("ue4ss/Mods/shared/UEHelpers/UEHelpers.lua", "helpers"),
            ("ue4ss/Mods/shared/ConfigManager/ConfigManager.lua", "pack-shared"),
            ("ue4ss/Mods/shared/ModMenu/ModMenu.lua", "menu"));
        var handheld = TestZip.CreateNamed(zips, "MortalShell2Mod-Handheld.zip",
            ("MortalShell2Mod/Scripts/main.lua", "handheld"),
            ("MortalShell2Mod/handheld.lua", "stick"),
            ("shared/ConfigManager/ConfigManager.lua", "handheld-shared"));
        var desktop = TestZip.CreateNamed(zips, "MortalShell2Mod.zip",
            ("MortalShell2Mod/Scripts/main.lua", "desktop"),
            ("shared/ConfigManager/ConfigManager.lua", "desktop-shared"));
        return (pack, handheld, desktop);
    }

    private static string LuaMain(string win64)
        => Path.Combine(win64, "ue4ss", "Mods", "MortalShell2Mod", "Scripts", "main.lua");

    private static string HandheldOnly(string win64)
        => Path.Combine(win64, "ue4ss", "Mods", "MortalShell2Mod", "handheld.lua");

    private static void AssertWiped(string win64, string? staleId = null)
    {
        Assert.False(Directory.Exists(Path.Combine(win64, "ue4ss")));
        Assert.False(File.Exists(Path.Combine(win64, "dwmapi.dll")));
        Assert.False(File.Exists(Path.Combine(win64, "UE4SS.dll")));
        Assert.Empty(ModTracker.List(win64));
        Assert.Equal(InstallKind.None, InstallTracker.Detect(win64).Kind);
        Assert.Null(InstallTracker.Detect(win64).GameBadge);

        var state = MainWindow.GetInstalledModsState(win64, staleId);
        Assert.Empty(state.Mods);
        Assert.Null(state.Selected);
    }
}
