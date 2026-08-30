using UE4SSInstaller.Services;

namespace UE4SSInstaller.Tests;

public sealed class Ue4ssInstallTests
{
    [Fact]
    public void Channel_switch_deletes_files_not_in_the_new_zip()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);

        var release = TestZip.Create(temp.Path,
            ("dwmapi.dll", "proxy"),
            ("ue4ss/UE4SS.dll", "core"),
            ("ue4ss/zdev-only.txt", "zdev"));
        ZipInstaller.InstallUe4ss(release, win64, Ue4ssChannel.ZDev);
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "zdev-only.txt")));

        var next = TestZip.Create(temp.Path,
            ("dwmapi.dll", "proxy"),
            ("ue4ss/UE4SS.dll", "core"));
        ZipInstaller.InstallUe4ss(next, win64, Ue4ssChannel.Release);

        Assert.False(File.Exists(Path.Combine(win64, "ue4ss", "zdev-only.txt")));
        Assert.True(File.Exists(Path.Combine(win64, "dwmapi.dll")));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "UE4SS.dll")));
    }

    [Fact]
    public void Same_channel_keeps_existing_settings()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);

        var first = TestZip.Create(temp.Path,
            ("dwmapi.dll", "proxy"),
            ("ue4ss/UE4SS-settings.ini", "GuiEnabled = 1"));
        ZipInstaller.InstallUe4ss(first, win64, Ue4ssChannel.Release);

        var settings = Path.Combine(win64, "ue4ss", "UE4SS-settings.ini");
        File.WriteAllText(settings, "GuiEnabled = 0");

        var second = TestZip.Create(temp.Path,
            ("dwmapi.dll", "proxy"),
            ("ue4ss/UE4SS-settings.ini", "GuiEnabled = 1"));
        ZipInstaller.InstallUe4ss(second, win64, Ue4ssChannel.Release);

        Assert.Equal("GuiEnabled = 0", File.ReadAllText(settings));
    }

    [Fact]
    public void Uninstall_removes_ue4ss_and_proxy_dlls_but_not_files_outside_win64()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Game", "Binaries", "Win64");
        var saved = temp.Combine("Game", "Saved", "SaveGame.sav");
        Directory.CreateDirectory(win64);
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        File.WriteAllText(saved, "keep me");

        var zip = TestZip.Create(temp.Path,
            ("dwmapi.dll", "proxy"),
            ("ue4ss/UE4SS.dll", "core"),
            ("ue4ss/Mods/HandCopied/main.lua", "mod"));
        ZipInstaller.InstallUe4ss(zip, win64, Ue4ssChannel.Release);
        ZipInstaller.UninstallUe4ss(win64);

        Assert.False(File.Exists(Path.Combine(win64, "dwmapi.dll")));
        Assert.False(Directory.Exists(Path.Combine(win64, "ue4ss")));
        Assert.True(File.Exists(saved));
        Assert.Empty(ModTracker.List(win64));
    }

    [Fact]
    public void Uninstall_clears_a_leftover_mods_list_at_win64_root()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);
        File.WriteAllText(Path.Combine(win64, ModsManifest.FileName),
            """{"mods":[{"id":"1","name":"Ghost","kind":"ModsFolder","files":[]}]}""");

        ZipInstaller.UninstallUe4ss(win64);

        Assert.False(File.Exists(Path.Combine(win64, ModsManifest.FileName)));
        Assert.Empty(ModTracker.List(win64));
    }

    [Fact]
    public void Uninstall_clears_read_only_files_inside_ue4ss()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);

        var zip = TestZip.Create(temp.Path,
            ("dwmapi.dll", "proxy"),
            ("ue4ss/UE4SS.dll", "core"),
            ("ue4ss/locked.txt", "keep-out"));
        ZipInstaller.InstallUe4ss(zip, win64, Ue4ssChannel.Release);

        var locked = Path.Combine(win64, "ue4ss", "locked.txt");
        File.SetAttributes(locked, File.GetAttributes(locked) | FileAttributes.ReadOnly);

        ZipInstaller.UninstallUe4ss(win64);

        Assert.False(Directory.Exists(Path.Combine(win64, "ue4ss")));
        Assert.False(File.Exists(Path.Combine(win64, "dwmapi.dll")));
        Assert.Empty(ModTracker.List(win64));
        Assert.Equal(InstallKind.None, InstallTracker.Detect(win64).Kind);
    }

    [Fact]
    public void ApplyInstallState_clears_a_stale_release_badge_after_uninstall()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);

        var zip = TestZip.Create(temp.Path,
            ("dwmapi.dll", "proxy"),
            ("ue4ss/UE4SS.dll", "core"));
        ZipInstaller.InstallUe4ss(zip, win64, Ue4ssChannel.Release);

        var game = new DetectedGame
        {
            Name = "Mortal Shell II",
            InstallPath = temp.Path,
            Win64Path = win64
        };
        game.ApplyInstallState(InstallTracker.Detect(win64));
        Assert.Equal("Release", game.ChannelLabel);
        Assert.True(game.HasChannelLabel);

        var notified = new List<string>();
        game.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
                notified.Add(args.PropertyName);
        };

        ZipInstaller.UninstallUe4ss(win64);
        game.ApplyInstallState(InstallTracker.Detect(win64));

        Assert.Equal(InstallKind.None, InstallTracker.Detect(win64).Kind);
        Assert.Null(game.ChannelLabel);
        Assert.False(game.HasChannelLabel);
        Assert.Contains(nameof(DetectedGame.ChannelLabel), notified);
        Assert.Contains(nameof(DetectedGame.HasChannelLabel), notified);
    }

    [Fact]
    public void Uninstall_ue4ss_clears_installed_mods_state_so_the_combo_has_no_selection()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);

        var ue4ss = TestZip.Create(temp.Path,
            ("dwmapi.dll", "proxy"),
            ("ue4ss/UE4SS.dll", "core"));
        ZipInstaller.InstallUe4ss(ue4ss, win64, Ue4ssChannel.Release);

        var modZip = TestZip.CreateNamed(temp.Combine("zips"), "CoolMod.zip",
            ("CoolMod/Scripts/main.lua", "hi"));
        ZipInstaller.InstallMod(modZip, win64);

        var before = MainWindow.GetInstalledModsState(win64);
        Assert.Equal("CoolMod", Assert.Single(before.Mods).Name);
        Assert.NotNull(before.Selected);
        Assert.Equal("CoolMod", before.Selected!.Name);

        var staleId = before.Selected.Id;
        ZipInstaller.UninstallUe4ss(win64);

        var after = MainWindow.GetInstalledModsState(win64, staleId);
        Assert.Empty(after.Mods);
        Assert.Null(after.Selected);
    }

    [Fact]
    public void Does_not_extract_zip_entries_outside_win64()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);

        var zip = TestZip.Create(temp.Path,
            ("dwmapi.dll", "proxy"),
            ("../../escaped.txt", "nope"));
        ZipInstaller.InstallUe4ss(zip, win64, Ue4ssChannel.Release);

        Assert.False(File.Exists(temp.Combine("escaped.txt")));
        Assert.True(File.Exists(Path.Combine(win64, "dwmapi.dll")));
    }

    [Fact]
    public void Unwraps_a_zdev_wrapper_folder_into_win64()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(win64);

        var zip = TestZip.Create(temp.Path,
            ("UE4SS-Palworld_zDev/dwmapi.dll", "proxy"),
            ("UE4SS-Palworld_zDev/ue4ss/UE4SS.dll", "core"),
            ("UE4SS-Palworld_zDev/ue4ss/UE4SS-settings.ini", "GuiEnabled = 1"));
        ZipInstaller.InstallUe4ss(zip, win64, Ue4ssChannel.ZDev);

        Assert.True(File.Exists(Path.Combine(win64, "dwmapi.dll")));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "UE4SS.dll")));
        Assert.False(Directory.Exists(Path.Combine(win64, "UE4SS-Palworld_zDev")));

        var manifest = InstallTracker.TryLoad(win64);
        Assert.Contains("dwmapi.dll", manifest!.Files);
        Assert.DoesNotContain(manifest.Files, f => f.StartsWith("UE4SS-Palworld_zDev", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Uninstall_removes_a_leftover_wrapper_folder_from_an_old_manifest()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        var wrapper = Path.Combine(win64, "UE4SS-Palworld_zDev");
        var nestedUe4ss = Path.Combine(wrapper, "ue4ss");
        Directory.CreateDirectory(nestedUe4ss);
        File.WriteAllText(Path.Combine(wrapper, "dwmapi.dll"), "proxy");
        File.WriteAllText(Path.Combine(nestedUe4ss, "UE4SS.dll"), "core");
        Directory.CreateDirectory(Path.Combine(win64, "ue4ss"));
        InstallTracker.Save(win64, new InstallerManifest
        {
            Channel = Ue4ssChannel.ZDev,
            Files =
            [
                "UE4SS-Palworld_zDev/dwmapi.dll",
                "UE4SS-Palworld_zDev/ue4ss/UE4SS.dll"
            ]
        });

        ZipInstaller.UninstallUe4ss(win64);

        Assert.False(Directory.Exists(wrapper));
        Assert.False(Directory.Exists(Path.Combine(win64, "ue4ss")));
        Assert.False(File.Exists(Path.Combine(win64, "dwmapi.dll")));
    }
}
