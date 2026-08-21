using UE4SSInstaller.Services;

namespace UE4SSInstaller.Tests;

public sealed class ModTrackerTests
{
    [Fact]
    public void Replacing_the_same_name_keeps_a_single_record()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Win64");
        Directory.CreateDirectory(Path.Combine(win64, "ue4ss"));

        ModTracker.SaveMod(win64, new InstalledMod
        {
            Name = "CoolMod",
            Kind = ModPackageKind.ModsFolder,
            Files = ["ue4ss/Mods/CoolMod/a.lua"]
        });
        ModTracker.SaveMod(win64, new InstalledMod
        {
            Name = "CoolMod",
            Kind = ModPackageKind.ModsFolder,
            Files = ["ue4ss/Mods/CoolMod/b.lua"]
        });

        var listed = ModTracker.List(win64);
        Assert.Single(listed);
        Assert.Equal("ue4ss/Mods/CoolMod/b.lua", Assert.Single(listed[0].Files));
    }
}

public sealed class InstallTrackerTests
{
    [Fact]
    public void Detects_managed_unmanaged_and_missing_installs()
    {
        using var temp = new TempDir();
        var none = temp.Combine("none");
        Directory.CreateDirectory(none);
        Assert.Equal(InstallKind.None, InstallTracker.Detect(none).Kind);

        var unmanaged = temp.Combine("unmanaged");
        Directory.CreateDirectory(unmanaged);
        File.WriteAllText(Path.Combine(unmanaged, "dwmapi.dll"), "x");
        Assert.Equal(InstallKind.Unmanaged, InstallTracker.Detect(unmanaged).Kind);

        var managed = temp.Combine("managed");
        Directory.CreateDirectory(Path.Combine(managed, "ue4ss"));
        File.WriteAllText(Path.Combine(managed, "dwmapi.dll"), "x");
        InstallTracker.Save(managed, new InstallerManifest
        {
            Channel = Ue4ssChannel.ZDev,
            Files = ["dwmapi.dll"]
        });
        var state = InstallTracker.Detect(managed);
        Assert.Equal(InstallKind.Managed, state.Kind);
        Assert.Equal(Ue4ssChannel.ZDev, state.Channel);
    }
}
