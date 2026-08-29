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

    [Fact]
    public void Applies_hook_and_general_patches()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Win64");
        var ini = Path.Combine(win64, "ue4ss", "UE4SS-settings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(ini)!);
        File.WriteAllText(ini, "[General]\nbUseUObjectArrayCache = true\n\n[Hooks]\nHookBeginPlay = 1\nHookEngineTick = 1\n");

        SettingsIniPatcher.ApplyPatches(win64,
        [
            new IniPatch("Hooks", "HookBeginPlay", "0"),
            new IniPatch("Hooks", "HookEngineTick", "0"),
            new IniPatch("General", "bUseUObjectArrayCache", "false")
        ]);

        var text = File.ReadAllText(ini);
        Assert.Contains("HookBeginPlay = 0", text);
        Assert.Contains("HookEngineTick = 0", text);
        Assert.Contains("bUseUObjectArrayCache = false", text);
        Assert.DoesNotContain("HookBeginPlay = 1", text);
        Assert.DoesNotContain("bUseUObjectArrayCache = true", text);
    }

    [Fact]
    public void Reads_logging_and_cache_from_the_game_settings_file()
    {
        using var temp = new TempDir();
        var win64 = WriteIni(temp, """
            [General]
            ; Default: true
            bUseUObjectArrayCache = false

            [Debug]
            ; Whether to enable the external UE4SS debug console.
            ConsoleEnabled = 0
            GuiConsoleEnabled = 0
            GuiConsoleVisible = 0
            """);

        var settings = SettingsIniPatcher.TryReadRuntimeSettings(win64);
        Assert.NotNull(settings);
        Assert.False(settings.LoggingEnabled);
        Assert.False(settings.UseUObjectArrayCache);
    }

    [Fact]
    public void Logging_is_on_when_any_debug_console_key_is_enabled()
    {
        using var temp = new TempDir();
        var win64 = WriteIni(temp, """
            [General]
            bUseUObjectArrayCache = true

            [Debug]
            ConsoleEnabled = 0
            GuiConsoleEnabled = 1
            GuiConsoleVisible = 0
            """);

        var settings = SettingsIniPatcher.TryReadRuntimeSettings(win64);
        Assert.NotNull(settings);
        Assert.True(settings.LoggingEnabled);
        Assert.True(settings.UseUObjectArrayCache);
    }

    [Fact]
    public void Missing_keys_use_ue4ss_defaults_logging_off_and_cache_on()
    {
        using var temp = new TempDir();
        var win64 = WriteIni(temp, "[General]\nEnableHotReloadSystem = 0\n");

        var settings = SettingsIniPatcher.TryReadRuntimeSettings(win64);
        Assert.NotNull(settings);
        Assert.False(settings.LoggingEnabled);
        Assert.True(settings.UseUObjectArrayCache);
    }

    [Fact]
    public void Missing_settings_file_returns_null()
    {
        using var temp = new TempDir();
        var win64 = temp.Combine("Win64");
        Directory.CreateDirectory(win64);
        Assert.Null(SettingsIniPatcher.TryReadRuntimeSettings(win64));
    }

    [Fact]
    public void Enabling_logging_writes_all_three_debug_console_keys()
    {
        using var temp = new TempDir();
        var win64 = WriteIni(temp, """
            [General]
            bUseUObjectArrayCache = false

            [Debug]
            ConsoleEnabled = 0
            GuiConsoleEnabled = 0
            GuiConsoleVisible = 0
            GuiConsoleFontScaling = 1
            """);

        SettingsIniPatcher.SetLoggingEnabled(win64, true);
        var text = File.ReadAllText(Path.Combine(win64, "ue4ss", "UE4SS-settings.ini"));
        Assert.Contains("ConsoleEnabled = 1", text);
        Assert.Contains("GuiConsoleEnabled = 1", text);
        Assert.Contains("GuiConsoleVisible = 1", text);
        Assert.Contains("GuiConsoleFontScaling = 1", text);
        Assert.Contains("bUseUObjectArrayCache = false", text);
        Assert.DoesNotContain("ConsoleEnabled = 0", text);

        var settings = SettingsIniPatcher.TryReadRuntimeSettings(win64);
        Assert.NotNull(settings);
        Assert.True(settings.LoggingEnabled);
        Assert.False(settings.UseUObjectArrayCache);

        SettingsIniPatcher.SetLoggingEnabled(win64, false);
        settings = SettingsIniPatcher.TryReadRuntimeSettings(win64);
        Assert.NotNull(settings);
        Assert.False(settings.LoggingEnabled);
    }

    [Fact]
    public void Toggling_cache_keeps_debug_console_keys()
    {
        using var temp = new TempDir();
        var win64 = WriteIni(temp, """
            [General]
            ; Setting this to false can help if you're experiencing a crash on startup.
            bUseUObjectArrayCache = false

            [Debug]
            ConsoleEnabled = 1
            GuiConsoleEnabled = 1
            GuiConsoleVisible = 1
            """);

        SettingsIniPatcher.SetUObjectArrayCache(win64, true);
        var text = File.ReadAllText(Path.Combine(win64, "ue4ss", "UE4SS-settings.ini"));
        Assert.Contains("bUseUObjectArrayCache = true", text);
        Assert.DoesNotContain("bUseUObjectArrayCache = false", text);
        Assert.Contains("; Setting this to false can help if you're experiencing a crash on startup.", text);
        Assert.Contains("GuiConsoleEnabled = 1", text);

        var settings = SettingsIniPatcher.TryReadRuntimeSettings(win64);
        Assert.NotNull(settings);
        Assert.True(settings.UseUObjectArrayCache);
        Assert.True(settings.LoggingEnabled);
    }

    [Fact]
    public void Does_not_overwrite_a_commented_out_key()
    {
        using var temp = new TempDir();
        var win64 = WriteIni(temp, """
            [Debug]
            ; ConsoleEnabled = 1
            ConsoleEnabled = 0
            GuiConsoleEnabled = 0
            GuiConsoleVisible = 0
            """);

        SettingsIniPatcher.SetLoggingEnabled(win64, true);
        var text = File.ReadAllText(Path.Combine(win64, "ue4ss", "UE4SS-settings.ini"));
        Assert.Contains("; ConsoleEnabled = 1", text);
        Assert.Contains("ConsoleEnabled = 1", text);
        Assert.DoesNotContain("ConsoleEnabled = 0", text);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("", null)]
    public void Parses_ini_bools(string value, bool? expected)
        => Assert.Equal(expected, SettingsIniPatcher.TryParseBool(value));

    private static string WriteIni(TempDir temp, string contents)
    {
        var win64 = temp.Combine("Win64");
        var ini = Path.Combine(win64, "ue4ss", "UE4SS-settings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(ini)!);
        File.WriteAllText(ini, contents.ReplaceLineEndings("\n"));
        return win64;
    }
}
