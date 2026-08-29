using System.Text;

namespace UE4SSInstaller.Services;

public sealed record Ue4ssRuntimeSettings(bool LoggingEnabled, bool UseUObjectArrayCache);

public static class SettingsIniPatcher
{
    public const string GeneralSection = "General";
    public const string DebugSection = "Debug";
    public const string UObjectArrayCacheKey = "bUseUObjectArrayCache";
    public const string ConsoleEnabledKey = "ConsoleEnabled";
    public const string GuiConsoleEnabledKey = "GuiConsoleEnabled";
    public const string GuiConsoleVisibleKey = "GuiConsoleVisible";

    public static string? FindSettingsPath(string win64Path)
    {
        var nested = Path.Combine(win64Path, "ue4ss", "UE4SS-settings.ini");
        if (File.Exists(nested))
            return nested;

        var root = Path.Combine(win64Path, "UE4SS-settings.ini");
        return File.Exists(root) ? root : null;
    }

    public static void ApplyEngineVersion(string win64Path, int major, int minor)
    {
        var path = FindSettingsPath(win64Path)
                   ?? throw new InvalidOperationException("UE4SS-settings.ini was not found.");

        var lines = File.ReadAllLines(path).ToList();
        var section = IndexOfSection(lines, "EngineVersionOverride");
        if (section < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add("");
            lines.Add("[EngineVersionOverride]");
            lines.Add($"MajorVersion = {major}");
            lines.Add($"MinorVersion = {minor}");
        }
        else
        {
            SetKeyInSection(lines, section, "MajorVersion", major.ToString());
            SetKeyInSection(lines, section, "MinorVersion", minor.ToString());
        }

        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static void ApplyPatches(string win64Path, IReadOnlyList<IniPatch> patches)
    {
        if (patches.Count == 0)
            return;

        var path = FindSettingsPath(win64Path)
                   ?? throw new InvalidOperationException("UE4SS-settings.ini was not found.");

        var lines = File.ReadAllLines(path).ToList();
        foreach (var patch in patches)
        {
            var section = IndexOfSection(lines, patch.Section);
            if (section < 0)
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                    lines.Add("");
                lines.Add($"[{patch.Section}]");
                section = lines.Count - 1;
            }

            SetKeyInSection(lines, section, patch.Key, patch.Value);
        }

        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static Ue4ssRuntimeSettings? TryReadRuntimeSettings(string win64Path)
    {
        if (FindSettingsPath(win64Path) is null)
            return null;

        var logging = IsTruthy(TryReadValue(win64Path, DebugSection, ConsoleEnabledKey))
                      || IsTruthy(TryReadValue(win64Path, DebugSection, GuiConsoleEnabledKey))
                      || IsTruthy(TryReadValue(win64Path, DebugSection, GuiConsoleVisibleKey));
        var cache = TryParseBool(TryReadValue(win64Path, GeneralSection, UObjectArrayCacheKey)) ?? true;
        return new Ue4ssRuntimeSettings(logging, cache);
    }

    public static void SetLoggingEnabled(string win64Path, bool enabled)
    {
        var value = enabled ? "1" : "0";
        ApplyPatches(win64Path,
        [
            new IniPatch(DebugSection, ConsoleEnabledKey, value),
            new IniPatch(DebugSection, GuiConsoleEnabledKey, value),
            new IniPatch(DebugSection, GuiConsoleVisibleKey, value)
        ]);
    }

    public static void SetUObjectArrayCache(string win64Path, bool enabled)
        => ApplyPatches(win64Path, [new IniPatch(GeneralSection, UObjectArrayCacheKey, enabled ? "true" : "false")]);

    public static string? TryReadValue(string win64Path, string section, string key)
    {
        var path = FindSettingsPath(win64Path);
        if (path is null)
            return null;

        var lines = File.ReadAllLines(path).ToList();
        var sectionIndex = IndexOfSection(lines, section);
        if (sectionIndex < 0)
            return null;

        return GetKeyInSection(lines, sectionIndex, key);
    }

    internal static bool IsTruthy(string? value)
        => TryParseBool(value) == true;

    internal static bool? TryParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        if (text.Equals("true", StringComparison.OrdinalIgnoreCase)
            || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || text.Equals("on", StringComparison.OrdinalIgnoreCase)
            || text == "1")
        {
            return true;
        }

        if (text.Equals("false", StringComparison.OrdinalIgnoreCase)
            || text.Equals("no", StringComparison.OrdinalIgnoreCase)
            || text.Equals("off", StringComparison.OrdinalIgnoreCase)
            || text == "0")
        {
            return false;
        }

        return null;
    }

    private static int IndexOfSection(List<string> lines, string section)
    {
        var header = $"[{section}]";
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().Equals(header, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static string? GetKeyInSection(List<string> lines, int sectionIndex, string key)
    {
        for (var i = sectionIndex + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                break;
            if (!TryKeyValue(trimmed, out var foundKey, out var value))
                continue;
            if (foundKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    private static void SetKeyInSection(List<string> lines, int sectionIndex, string key, string value)
    {
        for (var i = sectionIndex + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                break;
            if (!TryKeyValue(trimmed, out var foundKey, out _))
                continue;
            if (!foundKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            var prefix = lines[i][..lines[i].IndexOf('=')];
            lines[i] = $"{prefix}= {value}";
            return;
        }

        lines.Insert(sectionIndex + 1, $"{key} = {value}");
    }

    private static bool TryKeyValue(string trimmed, out string key, out string value)
    {
        key = "";
        value = "";
        if (IsComment(trimmed))
            return false;

        var equals = trimmed.IndexOf('=');
        if (equals <= 0)
            return false;

        key = trimmed[..equals].Trim();
        if (key.Length == 0)
            return false;

        value = trimmed[(equals + 1)..].Trim();
        return true;
    }

    private static bool IsComment(string trimmed)
        => trimmed.StartsWith(';') || trimmed.StartsWith('#');
}
