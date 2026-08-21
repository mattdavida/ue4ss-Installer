using UE4SSInstaller.Services;

namespace UE4SSInstaller.Tests;

public sealed class SafeCombineTests
{
    [Fact]
    public void Accepts_a_file_under_the_root()
    {
        using var temp = new TempDir();
        var dest = ZipInstaller.SafeCombine(temp.Path, "ue4ss/UE4SS.dll");
        Assert.NotNull(dest);
        Assert.StartsWith(Path.GetFullPath(temp.Path), dest, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../../evil.dll")]
    [InlineData("..\\..\\evil.dll")]
    [InlineData("ue4ss/../../../Windows/evil.dll")]
    public void Rejects_paths_that_escape_the_root(string relative)
    {
        using var temp = new TempDir();
        Assert.Null(ZipInstaller.SafeCombine(temp.Path, relative));
    }
}
