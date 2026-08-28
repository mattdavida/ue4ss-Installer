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

    [Fact]
    public void Display_text_trims_collapses_whitespace_and_caps_length()
    {
        Assert.Null(InstalledMod.NormalizeDisplayText("   "));
        Assert.Equal("use with cheat menu", InstalledMod.NormalizeDisplayText("  use   with\ncheat menu  "));
        Assert.Equal(new string('a', InstalledMod.MaxDisplayLength),
            InstalledMod.NormalizeDisplayText(new string('a', InstalledMod.MaxDisplayLength + 10)));
    }

    [Fact]
    public void ApplyDisplay_keeps_identity_name_and_treats_matching_label_as_unset()
    {
        var mod = new InstalledMod { Name = "MortalShell2Mod" };
        mod.ApplyDisplay("Cheat menu", "use with zDev");
        Assert.Equal("MortalShell2Mod", mod.Name);
        Assert.Equal("Cheat menu", mod.Label);
        Assert.Equal("use with zDev", mod.Note);
        Assert.Equal("Cheat menu", mod.DisplayName);

        mod.ApplyDisplay("MortalShell2Mod", "  ");
        Assert.Null(mod.Label);
        Assert.Null(mod.Note);
        Assert.Equal("MortalShell2Mod", mod.DisplayName);
        Assert.False(mod.HasNote);
    }

    [Fact]
    public void UpdateDisplay_writes_label_and_note_without_changing_name()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Win64");
        Directory.CreateDirectory(Path.Combine(win64, "ue4ss"));

        var original = new InstalledMod
        {
            Name = "CoolMod",
            Kind = ModPackageKind.ModsFolder,
            Files = ["ue4ss/Mods/CoolMod/a.lua"]
        };
        ModTracker.SaveMod(win64, original);

        Assert.True(ModTracker.UpdateDisplay(win64, original.Id, "My notes pack", "keep enabled"));
        var listed = Assert.Single(ModTracker.List(win64));
        Assert.Equal("CoolMod", listed.Name);
        Assert.Equal("My notes pack", listed.Label);
        Assert.Equal("keep enabled", listed.Note);
        Assert.Equal(original.Id, listed.Id);
        Assert.Equal("ue4ss/Mods/CoolMod/a.lua", Assert.Single(listed.Files));
    }

    [Fact]
    public void Manifest_roundtrip_keeps_label_and_note()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Win64");
        Directory.CreateDirectory(Path.Combine(win64, "ue4ss"));

        ModTracker.SaveMod(win64, new InstalledMod
        {
            Name = "CoolMod",
            Label = "Loadout A",
            Note = "plus cheat menu",
            Kind = ModPackageKind.ModsFolder,
            Files = ["ue4ss/Mods/CoolMod/a.lua"]
        });

        var listed = Assert.Single(ModTracker.List(win64));
        Assert.Equal("Loadout A", listed.Label);
        Assert.Equal("plus cheat menu", listed.Note);
        Assert.Equal("Loadout A", listed.DisplayName);
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

    [Fact]
    public void Leftover_ue4ss_folder_without_dlls_still_counts_as_installed()
    {
        using var temp = new TempDir();
        var leftover = temp.Combine("leftover");
        Directory.CreateDirectory(Path.Combine(leftover, "ue4ss", "Mods"));
        Assert.Equal(InstallKind.Unmanaged, InstallTracker.Detect(leftover).Kind);
    }
}
