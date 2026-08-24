using UE4SSInstaller.Services;

namespace UE4SSInstaller.Tests;

public sealed class GitHubFetcherTests
{
    [Fact]
    public void Release_picks_the_newest_UE4SS_v_zip_and_ignores_zDev()
    {
        var assets = new List<GitHubAsset>
        {
            new() { Name = "zDEV-UE4SS_v3.0.1-999.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-21T00:00:00Z") },
            new() { Name = "UE4SS_v3.0.1-100.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z") },
            new() { Name = "UE4SS_v3.0.1-200.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-20T00:00:00Z") },
            new() { Name = "helper.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-22T00:00:00Z") }
        };

        var picked = GitHubFetcher.SelectAsset(assets, Ue4ssChannel.Release);
        Assert.Equal("UE4SS_v3.0.1-200.zip", picked?.Name);
    }

    [Fact]
    public void ZDev_picks_the_newest_zDEV_zip()
    {
        var assets = new List<GitHubAsset>
        {
            new() { Name = "UE4SS_v3.0.1-200.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-21T00:00:00Z") },
            new() { Name = "zDEV-UE4SS_v3.0.1-10.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z") },
            new() { Name = "zDEV-UE4SS_v3.0.1-20.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-20T00:00:00Z") }
        };

        var picked = GitHubFetcher.SelectAsset(assets, Ue4ssChannel.ZDev);
        Assert.Equal("zDEV-UE4SS_v3.0.1-20.zip", picked?.Name);
    }

    [Fact]
    public void Pin_picks_matching_git_sha_instead_of_newest()
    {
        var assets = new List<GitHubAsset>
        {
            new() { Name = "UE4SS_v3.0.1-1088-ga1e7f571.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-24T00:00:00Z") },
            new() { Name = "UE4SS_v3.0.1-1028-gd7e7826d.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-15T00:00:00Z") },
            new() { Name = "zDEV-UE4SS_v3.0.1-1088-ga1e7f571.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-24T00:00:00Z") }
        };

        var release = GitHubFetcher.SelectAsset(assets, Ue4ssChannel.Release, "d7e7826d");
        Assert.Equal("UE4SS_v3.0.1-1028-gd7e7826d.zip", release?.Name);

        var zdev = GitHubFetcher.SelectAsset(assets, Ue4ssChannel.ZDev, "gd7e7826d");
        Assert.Null(zdev);

        assets.Add(new GitHubAsset
        {
            Name = "zDEV-UE4SS_v3.0.1-1028-gd7e7826d.zip",
            UpdatedAt = DateTimeOffset.Parse("2026-08-15T00:00:00Z")
        });
        zdev = GitHubFetcher.SelectAsset(assets, Ue4ssChannel.ZDev, "d7e7826d");
        Assert.Equal("zDEV-UE4SS_v3.0.1-1028-gd7e7826d.zip", zdev?.Name);
    }

    [Fact]
    public void Pin_can_be_resolved_from_the_experimental_archive_when_latest_moved_on()
    {
        var latest = new List<GitHubAsset>
        {
            new() { Name = "UE4SS_v3.0.1-1088-ga1e7f571.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-24T00:00:00Z") },
            new() { Name = "zDEV-UE4SS_v3.0.1-1088-ga1e7f571.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-24T00:00:00Z") }
        };
        var archive = new List<GitHubAsset>
        {
            new() { Name = "UE4SS_v3.0.1-1028-gd7e7826d.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-15T00:00:00Z") },
            new() { Name = "zDEV-UE4SS_v3.0.1-1028-gd7e7826d.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-15T00:00:00Z") }
        };

        Assert.Null(GitHubFetcher.SelectAsset(latest, Ue4ssChannel.ZDev, "d7e7826d"));
        Assert.Equal(
            "zDEV-UE4SS_v3.0.1-1028-gd7e7826d.zip",
            GitHubFetcher.SelectAsset(archive, Ue4ssChannel.ZDev, "d7e7826d")?.Name);
        Assert.Equal(
            "UE4SS_v3.0.1-1028-gd7e7826d.zip",
            GitHubFetcher.SelectAsset(archive, Ue4ssChannel.Release, "d7e7826d")?.Name);
    }

    [Fact]
    public void Pin_returns_null_when_sha_is_missing()
    {
        var assets = new List<GitHubAsset>
        {
            new() { Name = "UE4SS_v3.0.1-1088-ga1e7f571.zip", UpdatedAt = DateTimeOffset.Parse("2026-08-24T00:00:00Z") }
        };

        Assert.Null(GitHubFetcher.SelectAsset(assets, Ue4ssChannel.Release, "d7e7826d"));
    }

    [Fact]
    public void Complete_download_is_accepted()
    {
        using var temp = new TempDir();
        var file = temp.Combine("asset.zip");
        File.WriteAllBytes(file, [1, 2, 3, 4]);
        GitHubFetcher.EnsureCompleteDownload(file, expectedSize: 4);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Incomplete_download_is_deleted_and_throws()
    {
        using var temp = new TempDir();
        var file = temp.Combine("asset.zip");
        File.WriteAllBytes(file, [1, 2]);
        var ex = Assert.Throws<IOException>(() => GitHubFetcher.EnsureCompleteDownload(file, expectedSize: 99));
        Assert.Contains("didn't finish", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Missing_size_is_skipped()
    {
        using var temp = new TempDir();
        var file = temp.Combine("asset.zip");
        File.WriteAllBytes(file, [1]);
        GitHubFetcher.EnsureCompleteDownload(file, expectedSize: 0);
        Assert.True(File.Exists(file));
    }
}
