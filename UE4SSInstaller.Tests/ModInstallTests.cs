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

        var zip = TestZip.Create(temp.Path,
            ("MyMod/Scripts/main.lua", "print('hi')"));
        var result = ZipInstaller.InstallMod(zip, win64);

        var installed = Path.Combine(win64, "ue4ss", "Mods", "MyMod", "Scripts", "main.lua");
        Assert.True(File.Exists(installed));
        Assert.Equal(ModPackageKind.ModsFolder, result.Kind);
        Assert.Contains("ue4ss/Mods/MyMod/Scripts/main.lua", result.Files, StringComparer.OrdinalIgnoreCase);

        var mod = Assert.Single(ModTracker.List(win64));
        ZipInstaller.UninstallMod(win64, mod.Id);

        Assert.False(File.Exists(installed));
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "UE4SS.dll")));
        Assert.Empty(ModTracker.List(win64));
    }

    [Fact]
    public void Reinstalling_the_same_zip_name_drops_files_removed_from_the_new_zip()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Binaries", "Win64");
        Directory.CreateDirectory(Path.Combine(win64, "ue4ss"));

        var zips = temp.Combine("zips");
        Directory.CreateDirectory(zips);
        var first = Path.Combine(zips, "CoolMod.zip");
        File.Copy(TestZip.Create(temp.Path,
            ("CoolMod/a.lua", "a"),
            ("CoolMod/old.lua", "old")), first);

        ZipInstaller.InstallMod(first, win64);
        Assert.True(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "CoolMod", "old.lua")));

        File.Delete(first);
        File.Copy(TestZip.Create(temp.Path, ("CoolMod/a.lua", "a2")), first);
        ZipInstaller.InstallMod(first, win64);

        Assert.False(File.Exists(Path.Combine(win64, "ue4ss", "Mods", "CoolMod", "old.lua")));
        Assert.Equal("a2", File.ReadAllText(Path.Combine(win64, "ue4ss", "Mods", "CoolMod", "a.lua")));
        Assert.Single(ModTracker.List(win64));
    }
}
