using UE4SSInstaller.Services;

namespace UE4SSInstaller.Tests;

public sealed class SettingsIniPatcherTests
{
    [Fact]
    public void Adds_EngineVersionOverride_when_missing()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Win64");
        var ini = Path.Combine(win64, "ue4ss", "UE4SS-settings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(ini)!);
        File.WriteAllText(ini, "[General]\nGuiEnabled = 1\n");

        SettingsIniPatcher.ApplyEngineVersion(win64, 4, 27);
        var text = File.ReadAllText(ini);
        Assert.Contains("[EngineVersionOverride]", text);
        Assert.Contains("MajorVersion = 4", text);
        Assert.Contains("MinorVersion = 27", text);
    }

    [Fact]
    public void Updates_existing_override_keys()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Win64");
        var ini = Path.Combine(win64, "ue4ss", "UE4SS-settings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(ini)!);
        File.WriteAllText(ini, "[EngineVersionOverride]\nMajorVersion = 5\nMinorVersion = 1\n");

        SettingsIniPatcher.ApplyEngineVersion(win64, 4, 27);
        var text = File.ReadAllText(ini);
        Assert.Contains("MajorVersion = 4", text);
        Assert.Contains("MinorVersion = 27", text);
        Assert.DoesNotContain("MajorVersion = 5", text);
    }
}
