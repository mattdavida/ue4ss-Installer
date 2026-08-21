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
}
