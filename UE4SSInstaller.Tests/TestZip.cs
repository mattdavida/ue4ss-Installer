using System.IO.Compression;

namespace UE4SSInstaller.Tests;

internal static class TestZip
{
    public static string Create(string directory, params (string Entry, string Content)[] files)
        => CreateNamed(directory, Guid.NewGuid().ToString("N") + ".zip", files);

    public static string CreateNamed(string directory, string fileName, params (string Entry, string Content)[] files)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        if (File.Exists(path))
            File.Delete(path);

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in files)
        {
            var item = archive.CreateEntry(entry, CompressionLevel.NoCompression);
            using var writer = new StreamWriter(item.Open());
            writer.Write(content);
        }

        return path;
    }
}
