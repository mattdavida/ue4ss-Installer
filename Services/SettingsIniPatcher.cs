using System.Text;

namespace UE4SSInstaller.Services;

public static class SettingsIniPatcher
{
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

    private static void SetKeyInSection(List<string> lines, int sectionIndex, string key, string value)
    {
        for (var i = sectionIndex + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                break;

            var equals = trimmed.IndexOf('=');
            if (equals <= 0)
                continue;

            if (!trimmed[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            var prefix = lines[i][..lines[i].IndexOf('=')];
            lines[i] = $"{prefix}= {value}";
            return;
        }

        lines.Insert(sectionIndex + 1, $"{key} = {value}");
    }
}
