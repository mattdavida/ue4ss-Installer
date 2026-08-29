using UE4SSInstaller.Services;

namespace UE4SSInstaller.Tests;

public sealed class ModInstallTests
{
    [Fact]
    public void Installs_a_mod_zip_into_ue4ss_Mods_and_uninstall_removes_only_those_files()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(Path.Combine(win64, "ue4ss"));
        File.WriteAllText(Path.Combine(win64, "ue4ss", "UE4SS.dll"), "core");

        var zip = TestZip.CreateNamed(temp.Path, "MyMod.zip",
            ("MyMod/Scripts/main.lua", "print('hi')"));
        var result = ZipInstaller.InstallMod(zip, win64);

        var installed = Path.Combine(win64, "ue4ss", "Mods", "MyMod", "Scripts", "main.lua");
        Assert.True(File.Exists(installed));
        Assert.Equal(ModPackageKind.ModsFolder, result.Kind);
        Assert.False(result.Reinstalled);
        Assert.Contains("ue4ss/Mods/MyMod/Scripts/main.lua", result.Files, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Installed MyMod into the Mods folder.", ZipInstaller.FormatModInstallStatus(result));

        var mod = Assert.Single(ModTracker.List(win64));
        ZipInstaller.UninstallMod(win64, mod.Id);

        Assert.False(File.Exists(installed));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "UE4SS.dll")));
        Assert.Empty(ModTracker.List(win64));
    }

    [Fact]
    public void Uninstall_and_reinstall_drop_configmanager_config_json_beside_Scripts()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        File.WriteAllText(Path.Combine(win64, "ue4ss", "UE4SS.dll"), "core");
        var zip = NamedModZip(temp, "MortalShell2Mod.zip",
            ("MortalShell2Mod/Scripts/main.lua", "print('hi')"),
            ("MortalShell2Mod/LICENSE", "mit"));
        ZipInstaller.InstallMod(zip, win64);

        var modRoot = Path.Combine(win64, "ue4ss", "Mods", "MortalShell2Mod");
        var config = Path.Combine(modRoot, "config.json");
        var otherModConfig = Path.Combine(win64, "ue4ss", "Mods", "OtherMod", "config.json");
        File.WriteAllText(config, """{"unsafe":true}""");
        Directory.CreateDirectory(Path.GetDirectoryName(otherModConfig)!);
        File.WriteAllText(otherModConfig, """{"keep":true}""");

        var listed = Assert.Single(ModTracker.List(win64));
        ZipInstaller.UninstallMod(win64, listed.Id);
        Assert.False(File.Exists(config));
        Assert.False(Directory.Exists(modRoot));
        Assert.True(File.Exists(otherModConfig));

        ZipInstaller.InstallMod(zip, win64);
        File.WriteAllText(config, """{"unsafe":true,"oldKey":1}""");
        TestZip.CreateNamed(Path.GetDirectoryName(zip)!, "MortalShell2Mod.zip",
            ("MortalShell2Mod/Scripts/main.lua", "print('v2')"));
        var result = ZipInstaller.InstallMod(zip, win64);

        Assert.True(result.Reinstalled);
        Assert.False(File.Exists(config));
        Assert.Equal("print('v2')", File.ReadAllText(Path.Combine(modRoot, "Scripts", "main.lua")));
        Assert.True(File.Exists(otherModConfig));
    }

    [Fact]
    public void Reinstall_replaces_a_zipped_config_json_instead_of_keeping_generated_keys()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        var zip = NamedModZip(temp, "MortalShell2Mod.zip",
            ("MortalShell2Mod/Scripts/main.lua", "print('hi')"));
        ZipInstaller.InstallMod(zip, win64);

        var config = Path.Combine(win64, "ue4ss", "Mods", "MortalShell2Mod", "config.json");
        File.WriteAllText(config, """{"unsafe":true}""");
        TestZip.CreateNamed(Path.GetDirectoryName(zip)!, "MortalShell2Mod.zip",
            ("MortalShell2Mod/Scripts/main.lua", "print('v2')"),
            ("MortalShell2Mod/config.json", """{"unsafe":false}"""));
        ZipInstaller.InstallMod(zip, win64);

        Assert.Equal("""{"unsafe":false}""", File.ReadAllText(config));
    }

    [Fact]
    public void Reinstalling_the_same_zip_name_drops_files_removed_from_the_new_zip()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        var zip = NamedModZip(temp, "CoolMod.zip",
            ("CoolMod/a.lua", "a"),
            ("CoolMod/old.lua", "old"));

        ZipInstaller.InstallMod(zip, win64);
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "CoolMod", "old.lua")));

        TestZip.CreateNamed(Path.GetDirectoryName(zip)!, "CoolMod.zip", ("CoolMod/a.lua", "a2"));
        var result = ZipInstaller.InstallMod(zip, win64);

        Assert.True(result.Reinstalled);
        Assert.False(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "CoolMod", "old.lua")));
        Assert.Equal("a2", File.ReadAllText(Path.Combine(win64, "ue4ss", "Mods", "CoolMod", "a.lua")));
        Assert.Single(ModTracker.List(win64));
        Assert.Equal("Reinstalled CoolMod into the Mods folder.", ZipInstaller.FormatModInstallStatus(result));
    }

    [Fact]
    public void Reinstall_overwrites_read_only_files_and_keeps_the_same_mod_id()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        var zip = NamedModZip(temp, "CoolMod.zip", ("CoolMod/a.lua", "v1"));

        var first = ZipInstaller.InstallMod(zip, win64);
        var installed = Path.Combine(win64, "ue4ss", "Mods", "CoolMod", "a.lua");
        File.SetAttributes(installed, File.GetAttributes(installed) | FileAttributes.ReadOnly);
        var id = Assert.Single(ModTracker.List(win64)).Id;

        TestZip.CreateNamed(Path.GetDirectoryName(zip)!, "CoolMod.zip", ("CoolMod/a.lua", "v2"));
        var second = ZipInstaller.InstallMod(zip, win64);

        Assert.False(first.Reinstalled);
        Assert.True(second.Reinstalled);
        Assert.Equal("v2", File.ReadAllText(installed));
        Assert.Equal(id, Assert.Single(ModTracker.List(win64)).Id);
    }

    [Fact]
    public void Reinstall_aborts_when_old_files_are_locked_and_does_not_copy_the_new_zip()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        var zip = NamedModZip(temp, "CoolMod.zip",
            ("CoolMod/a.lua", "v1"),
            ("CoolMod/old.lua", "old"));
        ZipInstaller.InstallMod(zip, win64);

        var aLua = Path.Combine(win64, "ue4ss", "Mods", "CoolMod", "a.lua");
        var oldLua = Path.Combine(win64, "ue4ss", "Mods", "CoolMod", "old.lua");
        var added = Path.Combine(win64, "ue4ss", "Mods", "CoolMod", "new.lua");
        TestZip.CreateNamed(Path.GetDirectoryName(zip)!, "CoolMod.zip",
            ("CoolMod/a.lua", "v2"),
            ("CoolMod/new.lua", "new"));

        IOException ex;
        using (var locked = new FileStream(aLua, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ex = Assert.Throws<IOException>(() => ZipInstaller.InstallMod(zip, win64));
        }

        Assert.Contains("Close the game", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("v1", File.ReadAllText(aLua));
        Assert.True(File.Exists(oldLua));
        Assert.False(File.Exists(added));
        var tracked = Assert.Single(ModTracker.List(win64));
        Assert.Contains("ue4ss/Mods/CoolMod/old.lua", tracked.Files, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ue4ss/Mods/CoolMod/new.lua", tracked.Files, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reinstall_of_a_game_directory_overlay_replaces_the_previous_files()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        File.WriteAllText(Path.Combine(win64, "ue4ss", "UE4SS.dll"), "core");
        var zip = NamedModZip(temp, "SigPack.zip",
            ("ue4ss/UE4SS_Signatures/FName.lua", "old"),
            ("ue4ss/Mods/Extra/Scripts/main.lua", "extra"));

        ZipInstaller.InstallMod(zip, win64);
        TestZip.CreateNamed(Path.GetDirectoryName(zip)!, "SigPack.zip",
            ("ue4ss/UE4SS_Signatures/FName.lua", "new"));
        var result = ZipInstaller.InstallMod(zip, win64);

        Assert.True(result.Reinstalled);
        Assert.Equal(ModPackageKind.GameDirectory, result.Kind);
        Assert.Equal("new", File.ReadAllText(Path.Combine(win64, "ue4ss", "UE4SS_Signatures", "FName.lua")));
        Assert.False(Directory.Exists(Path.Combine(win64, "ue4ss", "Mods", "Extra")));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "UE4SS.dll")));
        Assert.Equal(
            "Reinstalled SigPack into the game folder (UE4SS pack / overlay).",
            ZipInstaller.FormatModInstallStatus(result));
    }

    [Fact]
    public void WouldReinstall_is_true_for_the_same_mod_folder_even_when_zip_names_differ()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        var zip = NamedModZip(temp, "CoolMod.zip", ("CoolMod/a.lua", "a"));
        var other = NamedModZip(temp, "OtherMod.zip", ("OtherMod/a.lua", "a"));
        var nexus = NamedModZip(temp, "CoolMod 20 6.6 2026-08-24T22-49Z Hdppafbn8 (1).zip",
            ("CoolMod/a.lua", "b"));

        Assert.False(ZipInstaller.WouldReinstall(zip, win64));
        ZipInstaller.InstallMod(zip, win64);
        Assert.True(ZipInstaller.WouldReinstall(zip, win64));
        Assert.True(ZipInstaller.WouldReinstall(nexus, win64));
        Assert.False(ZipInstaller.WouldReinstall(other, win64));
    }

    [Fact]
    public void Nexus_named_zips_with_the_same_inner_folder_install_once_under_the_folder_name()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        var first = NamedModZip(temp, "MortalShell2Mod 20 6.5 2026-08-23T12-31Z g7LLJwXZw (1).zip",
            ("MortalShell2Mod/Scripts/main.lua", "v1"),
            ("MortalShell2Mod/old.lua", "old"));
        var second = NamedModZip(temp, "MortalShell2Mod 20 6.6 2026-08-24T22-49Z Hdppafbn8 (1).zip",
            ("MortalShell2Mod/Scripts/main.lua", "v2"));

        var installed = ZipInstaller.InstallMod(first, win64);
        Assert.False(installed.Reinstalled);
        Assert.Equal("MortalShell2Mod", installed.Name);
        Assert.Equal("MortalShell2Mod", Assert.Single(ModTracker.List(win64)).Name);

        var preview = ZipInstaller.PreviewModInstall(second, win64);
        Assert.Equal("MortalShell2Mod", preview.Name);
        Assert.True(preview.WouldReinstall);

        var result = ZipInstaller.InstallMod(second, win64);
        Assert.True(result.Reinstalled);
        Assert.Equal("MortalShell2Mod", result.Name);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(win64, "ue4ss", "Mods", "MortalShell2Mod", "Scripts", "main.lua")));
        Assert.False(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "MortalShell2Mod", "old.lua")));
        Assert.Equal("MortalShell2Mod", Assert.Single(ModTracker.List(win64)).Name);
        Assert.Equal("Reinstalled MortalShell2Mod into the Mods folder.", ZipInstaller.FormatModInstallStatus(result));
    }

    [Fact]
    public void Reinstall_keeps_a_custom_label_and_note()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        var first = NamedModZip(temp, "MortalShell2Mod.zip",
            ("MortalShell2Mod/Scripts/main.lua", "v1"));
        var second = NamedModZip(temp, "MortalShell2Mod 20 6.6 2026-08-24T22-49Z Hdppafbn8.zip",
            ("MortalShell2Mod/Scripts/main.lua", "v2"));

        ZipInstaller.InstallMod(first, win64);
        var originalId = Assert.Single(ModTracker.List(win64)).Id;
        Assert.True(ModTracker.UpdateDisplay(win64, originalId, "Cheat menu", "use with zDev"));

        var result = ZipInstaller.InstallMod(second, win64);
        Assert.True(result.Reinstalled);
        Assert.Equal("MortalShell2Mod", result.Name);

        var listed = Assert.Single(ModTracker.List(win64));
        Assert.Equal(originalId, listed.Id);
        Assert.Equal("MortalShell2Mod", listed.Name);
        Assert.Equal("Cheat menu", listed.Label);
        Assert.Equal("use with zDev", listed.Note);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(win64, "ue4ss", "Mods", "MortalShell2Mod", "Scripts", "main.lua")));
    }

    [Fact]
    public void Reinstall_collapses_already_tracked_nexus_names_that_share_a_mod_folder()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        var scripts = Path.Combine(win64, "ue4ss", "Mods", "MortalShell2Mod", "Scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "main.lua"), "old");
        File.WriteAllText(Path.Combine(win64, "ue4ss", "Mods", "MortalShell2Mod", "old.lua"), "leftover");

        ModTracker.SaveMod(win64, new InstalledMod
        {
            Name = "MortalShell2Mod 20 6.5 2026-08-23T12-31Z g7LLJwXZw (1)",
            Kind = ModPackageKind.ModsFolder,
            Files =
            [
                "ue4ss/Mods/MortalShell2Mod/Scripts/main.lua",
                "ue4ss/Mods/MortalShell2Mod/old.lua"
            ]
        });
        ModTracker.SaveMod(win64, new InstalledMod
        {
            Name = "MortalShell2 Ue4ss 20 6.6 2026-08-24T22-49Z 2kvvAthwD (1)",
            Kind = ModPackageKind.ModsFolder,
            Files = ["ue4ss/Mods/MortalShell2Mod/Scripts/main.lua"]
        });

        var zip = NamedModZip(temp, "MortalShell2Mod.zip",
            ("MortalShell2Mod/Scripts/main.lua", "new"));
        var result = ZipInstaller.InstallMod(zip, win64);

        Assert.True(result.Reinstalled);
        Assert.Equal("MortalShell2Mod", result.Name);
        Assert.Equal("new", File.ReadAllText(Path.Combine(scripts, "main.lua")));
        Assert.False(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "MortalShell2Mod", "old.lua")));
        Assert.Equal("MortalShell2Mod", Assert.Single(ModTracker.List(win64)).Name);
    }

    [Fact]
    public void Display_name_strips_nexus_tokens_when_the_inner_folder_is_the_download_name()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        var folder = "MortalShell2Mod 20 6.6 2026-08-24T22-49Z Hdppafbn8 (1)";
        var zip = NamedModZip(temp, folder + ".zip",
            ($"{folder}/Scripts/main.lua", "v1"));

        var result = ZipInstaller.InstallMod(zip, win64);

        Assert.Equal("MortalShell2Mod", result.Name);
        Assert.Equal("MortalShell2Mod", Assert.Single(ModTracker.List(win64)).Name);
        Assert.Equal(
            "Installed MortalShell2Mod into the Mods folder.",
            ZipInstaller.FormatModInstallStatus(result));
    }

    [Fact]
    public void Display_name_keeps_trailing_numbers_that_are_part_of_the_real_mod_name()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        var zip = NamedModZip(temp, "Half Life 2.zip",
            ("Half Life 2/Scripts/main.lua", "v1"));

        Assert.Equal("Half Life 2", ZipInstaller.InstallMod(zip, win64).Name);
    }

    [Theory]
    [InlineData("MortalShell2Mod.zip", "MortalShell2Mod")]
    [InlineData("CoolMod (1).zip", "CoolMod")]
    [InlineData("MortalShell2Mod 20 6.6 2026-08-24T22-49Z Hdppafbn8 (1).zip", "MortalShell2Mod")]
    [InlineData("MortalShell2Mod 20 6.7 2026-08-27T10-57Z UHuuZiksP.zip", "MortalShell2Mod")]
    [InlineData("CoolMod MortalShell.zip", "CoolMod MortalShell")]
    [InlineData("Half Life 2.zip", "Half Life 2")]
    public void CleanZipStem_strips_nexus_download_noise_but_keeps_real_names(string fileName, string expected)
    {
        using var temp = new TempDir();
        Assert.Equal(expected, ZipInstaller.CleanZipStem(Path.Combine(temp.Path, fileName)));
    }

    [Fact]
    public void Confirm_copy_uses_reinstall_wording_when_replacing_an_existing_mod()
    {
        using var temp = new TempDir();
        var mods = TestZip.CreateNamed(temp.Path, "CoolMod.zip", ("CoolMod/a.lua", "a"));
        var overlay = TestZip.CreateNamed(temp.Path, "SigPack.zip",
            ("ue4ss/UE4SS_Signatures/FName.lua", "sig"));

        Assert.Equal(
            "This installs CoolMod into the Mods folder. You can uninstall it later from Installed mods.",
            MainWindow.DescribeModZip(mods, "CoolMod", reinstall: false));
        Assert.Equal(
            "This replaces the existing CoolMod install. Old files are removed first, then the new zip is copied into the Mods folder.",
            MainWindow.DescribeModZip(mods, "CoolMod", reinstall: true));
        Assert.Equal(
            "This replaces the existing SigPack install. Old files are removed first, then the new zip is copied into the game folder.",
            MainWindow.DescribeModZip(overlay, "SigPack", reinstall: true));
    }

    [Fact]
    public void User_mod_that_ships_shared_does_not_replace_a_ue4ss_overlay()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        var overlay = NamedModZip(temp, "UE4SS For Star Wars Zero Company 9 1.0 2026-08-27T16-44Z 3SLfClRiS.zip",
            ("dwmapi.dll", "proxy"),
            ("ue4ss/UE4SS.dll", "core"),
            ("ue4ss/Mods/ConsoleEnablerMod/Scripts/main.lua", "console"),
            ("ue4ss/Mods/Keybinds/Scripts/main.lua", "keys"),
            ("ue4ss/Mods/shared/UEHelpers/UEHelpers.lua", "helpers"),
            ("ue4ss/Mods/mods.txt", "ConsoleEnablerMod : 1"));
        var userMod = NamedModZip(temp, "DevToolsMod.zip",
            ("DevToolsMod/Scripts/main.lua", "devtools"),
            ("DevToolsMod/enabled.txt", "1"),
            ("shared/ConfigManager/ConfigManager.lua", "config"),
            ("shared/ModMenu/ModMenu.lua", "menu"));

        var overlayResult = ZipInstaller.InstallMod(overlay, win64);
        Assert.False(overlayResult.Reinstalled);
        Assert.Equal(ModPackageKind.GameDirectory, overlayResult.Kind);
        Assert.Equal("UE4SS For Star Wars Zero Company", overlayResult.Name);
        Assert.False(ZipInstaller.WouldReinstall(userMod, win64));

        var preview = ZipInstaller.PreviewModInstall(userMod, win64);
        Assert.Equal("DevToolsMod", preview.Name);
        Assert.Equal(ModPackageKind.ModsFolder, preview.Kind);
        Assert.False(preview.WouldReinstall);

        var result = ZipInstaller.InstallMod(userMod, win64);
        Assert.False(result.Reinstalled);
        Assert.Equal("DevToolsMod", result.Name);
        Assert.Equal(ModPackageKind.ModsFolder, result.Kind);

        Assert.True(File.Exists(Path.Combine(win64, "dwmapi.dll")));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "UE4SS.dll")));
        Assert.Equal("helpers", File.ReadAllText(Path.Combine(win64, "ue4ss", "Mods", "shared", "UEHelpers", "UEHelpers.lua")));
        Assert.Equal("console", File.ReadAllText(Path.Combine(win64, "ue4ss", "Mods", "ConsoleEnablerMod", "Scripts", "main.lua")));
        Assert.Equal("devtools", File.ReadAllText(Path.Combine(win64, "ue4ss", "Mods", "DevToolsMod", "Scripts", "main.lua")));
        Assert.Equal("config", File.ReadAllText(Path.Combine(win64, "ue4ss", "Mods", "shared", "ConfigManager", "ConfigManager.lua")));

        var listed = ModTracker.List(win64).Select(mod => mod.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(["DevToolsMod", "UE4SS For Star Wars Zero Company"], listed);
        Assert.Equal(
            "Installed DevToolsMod into the Mods folder.",
            ZipInstaller.FormatModInstallStatus(result));

        var detected = InstallTracker.Detect(win64);
        Assert.Equal(InstallKind.CustomMod, detected.Kind);
        Assert.Equal("UE4SS For Star Wars Zero Company", detected.CustomModName);
        Assert.Equal("via UE4SS For Star Wars Zero Company", detected.GameBadge);
        Assert.Equal("Installed via mod: UE4SS For Star Wars Zero Company", detected.GameBadgeTip);
    }

    [Fact]
    public void Two_user_mods_that_ship_shared_libraries_can_both_stay_installed()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        File.WriteAllText(Path.Combine(win64, "ue4ss", "UE4SS.dll"), "core");
        var first = NamedModZip(temp, "CoolMod.zip",
            ("CoolMod/Scripts/main.lua", "cool"),
            ("shared/ConfigManager/ConfigManager.lua", "v1"));
        var second = NamedModZip(temp, "OtherMod.zip",
            ("OtherMod/Scripts/main.lua", "other"),
            ("shared/ConfigManager/ConfigManager.lua", "v2"));

        Assert.False(ZipInstaller.InstallMod(first, win64).Reinstalled);
        Assert.False(ZipInstaller.WouldReinstall(second, win64));
        var result = ZipInstaller.InstallMod(second, win64);

        Assert.False(result.Reinstalled);
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "CoolMod", "Scripts", "main.lua")));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "OtherMod", "Scripts", "main.lua")));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(win64, "ue4ss", "Mods", "shared", "ConfigManager", "ConfigManager.lua")));
        Assert.Equal(2, ModTracker.List(win64).Count);

        var cool = Assert.Single(ModTracker.List(win64), mod => mod.Name == "CoolMod");
        ZipInstaller.UninstallMod(win64, cool.Id);
        Assert.False(Directory.Exists(Path.Combine(win64, "ue4ss", "Mods", "CoolMod")));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "shared", "ConfigManager", "ConfigManager.lua")));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "OtherMod", "Scripts", "main.lua")));
        Assert.Equal("OtherMod", Assert.Single(ModTracker.List(win64)).Name);
    }

    [Fact]
    public void Display_name_ignores_shared_and_uses_the_real_mod_folder()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        var zip = NamedModZip(temp, "MyTools.zip",
            ("DevToolsMod/Scripts/main.lua", "devtools"),
            ("shared/ConfigManager/ConfigManager.lua", "config"));

        Assert.Equal("DevToolsMod", ZipInstaller.InstallMod(zip, win64).Name);
    }

    [Fact]
    public void Mods_folder_zip_without_ue4ss_is_rejected_and_does_not_copy_files()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);
        var zip = NamedModZip(temp, "CoolMod.zip", ("CoolMod/Scripts/main.lua", "hi"));

        var ex = Assert.Throws<InvalidOperationException>(() => ZipInstaller.InstallMod(zip, win64));
        Assert.Equal(ZipInstaller.Ue4ssNotInstalledMessage, ex.Message);
        Assert.False(Directory.Exists(Path.Combine(win64, "Mods")));
        Assert.False(Directory.Exists(Path.Combine(win64, "ue4ss")));
        Assert.Empty(ModTracker.List(win64));
    }

    [Fact]
    public void Preview_of_a_mods_folder_zip_without_ue4ss_says_ue4ss_is_not_installed()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);
        var zip = NamedModZip(temp, "CoolMod.zip", ("CoolMod/Scripts/main.lua", "hi"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => ZipInstaller.PreviewModInstall(zip, win64));
        Assert.Equal(ZipInstaller.Ue4ssNotInstalledMessage, ex.Message);
    }

    [Fact]
    public void Game_directory_pack_still_installs_when_ue4ss_is_missing()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);
        var zip = NamedModZip(temp, "UE4SS-pack.zip",
            ("dwmapi.dll", "proxy"),
            ("ue4ss/UE4SS.dll", "core"),
            ("ue4ss/Mods/Included/Scripts/main.lua", "mod"));

        var result = ZipInstaller.InstallMod(zip, win64);

        Assert.Equal(ModPackageKind.GameDirectory, result.Kind);
        Assert.True(File.Exists(Path.Combine(win64, "dwmapi.dll")));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "UE4SS.dll")));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "Included", "Scripts", "main.lua")));
        Assert.Equal("Included", Assert.Single(ModTracker.List(win64)).Name);
    }

    [Fact]
    public void Uninstall_mod_without_ue4ss_says_ue4ss_is_not_installed()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);

        var ex = Assert.Throws<InvalidOperationException>(
            () => ZipInstaller.UninstallMod(win64, "missing"));
        Assert.Equal(ZipInstaller.Ue4ssNotInstalledMessage, ex.Message);
    }

    [Fact]
    public void Uninstall_unknown_mod_with_ue4ss_present_keeps_the_list_message()
    {
        using var temp = new TempDir();
        var win64 = PrepareWin64(temp);
        File.WriteAllText(Path.Combine(win64, "ue4ss", "UE4SS.dll"), "core");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ZipInstaller.UninstallMod(win64, "missing"));
        Assert.Equal("That mod is not in the installer list.", ex.Message);
    }

    private static string PrepareWin64(TempDir temp)
    {
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(Path.Combine(win64, "ue4ss"));
        return win64;
    }

    private static string NamedModZip(TempDir temp, string fileName, params (string Entry, string Content)[] files)
        => TestZip.CreateNamed(temp.Combine("zips"), fileName, files);
}
