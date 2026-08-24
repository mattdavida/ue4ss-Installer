using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UE4SSInstaller.Services;

public enum Ue4ssChannel
{
    Release,
    ZDev
}

public enum Ue4ssAssetStyle
{
    Official,
    Palworld
}

/// <summary>
/// GitHub release to pull a UE4SS zip from. Palworld uses a rolling community tag
/// that the Palworld docs keep pointing at; fetching that tag is how this app
/// stays current without scraping the wiki.
/// </summary>
public sealed record Ue4ssReleaseSource(
    string Owner,
    string Repo,
    string Tag,
    Ue4ssAssetStyle AssetStyle = Ue4ssAssetStyle.Official,
    string? ArchiveTag = null)
{
    public static Ue4ssReleaseSource OfficialExperimental { get; } = new(
        "UE4SS-RE",
        "RE-UE4SS",
        "experimental-latest",
        Ue4ssAssetStyle.Official,
        "experimental");

    public static Ue4ssReleaseSource Palworld { get; } = new(
        "Okaetsu",
        "RE-UE4SS",
        "experimental-palworld",
        Ue4ssAssetStyle.Palworld);

    public string Label => $"{Owner}/{Repo} {Tag}";
}

/// <summary>
/// Downloads UE4SS from GitHub. Default source is <c>experimental-latest</c>.
/// That tag only keeps the current build. A Git SHA pin falls back to the
/// <c>experimental</c> archive, which still has older zips such as <c>d7e7826d</c>.
/// Palworld uses <see cref="Ue4ssReleaseSource.Palworld"/> instead.
/// </summary>
public static class GitHubFetcher
{
    private static readonly Regex SafeRepoPart = new(
        @"^[A-Za-z0-9_.-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HttpClient Http = CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task<string> DownloadAsync(
        Ue4ssChannel channel,
        string? pinnedGitSha = null,
        Ue4ssReleaseSource? source = null,
        CancellationToken cancellationToken = default)
    {
        source ??= Ue4ssReleaseSource.OfficialExperimental;
        var release = await GetReleaseAsync(ReleaseTagUrl(source.Owner, source.Repo, source.Tag), cancellationToken);
        var asset = SelectAsset(release.Assets, channel, pinnedGitSha, source.AssetStyle);

        if (asset is null && pinnedGitSha is not null && !string.IsNullOrWhiteSpace(source.ArchiveTag))
        {
            release = await GetReleaseAsync(
                ReleaseTagUrl(source.Owner, source.Repo, source.ArchiveTag),
                cancellationToken);
            asset = SelectAsset(release.Assets, channel, pinnedGitSha, source.AssetStyle);
        }

        if (asset is null)
        {
            var pin = NormalizeGitSha(pinnedGitSha);
            throw new InvalidOperationException(pin is null
                ? $"No matching {channel} zip was found on {source.Label}."
                : $"Pinned UE4SS {pin} was not found on {source.Label} or {source.ArchiveTag} for {channel}.");
        }

        return await DownloadAssetAsync(asset, cancellationToken);
    }

    private static async Task<GitHubRelease> GetReleaseAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, cancellationToken);
        EnsureApiSuccess(response);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("GitHub returned an empty release payload.");
    }

    public static async Task<string> DownloadLatestReleaseZipAsync(
        string owner,
        string repo,
        CancellationToken cancellationToken = default)
    {
        if (!SafeRepoPart.IsMatch(owner) || !SafeRepoPart.IsMatch(repo))
            throw new InvalidOperationException("Invalid GitHub repository.");

        var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        using var response = await Http.GetAsync(url, cancellationToken);
        EnsureApiSuccess(response);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken)
                      ?? throw new InvalidOperationException("GitHub returned an empty release payload.");

        var asset = release.Assets
                        .Where(a => Path.GetFileName(a.Name)
                            .EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(a => a.UpdatedAt)
                        .ThenByDescending(a => a.CreatedAt)
                        .FirstOrDefault()
                    ?? throw new InvalidOperationException($"No zip was found on {owner}/{repo} latest release.");

        return await DownloadAssetAsync(asset, cancellationToken);
    }

    private static async Task<string> DownloadAssetAsync(GitHubAsset asset, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(asset.Name);
        if (string.IsNullOrWhiteSpace(fileName)
            || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("GitHub returned an unexpected asset name.");
        }

        EnsureTrustedDownloadUrl(asset.BrowserDownloadUrl);

        var downloadDir = Path.Combine(Path.GetTempPath(), "UE4SSInstaller");
        Directory.CreateDirectory(downloadDir);
        var destination = Path.Combine(downloadDir, fileName);

        await using (var remote = await Http.GetStreamAsync(asset.BrowserDownloadUrl, cancellationToken))
        await using (var file = File.Create(destination))
        {
            await remote.CopyToAsync(file, cancellationToken);
        }

        EnsureCompleteDownload(destination, asset.Size);
        return destination;
    }

    internal static void EnsureCompleteDownload(string path, long expectedSize)
    {
        if (expectedSize <= 0)
            return;

        var actual = new FileInfo(path).Length;
        if (actual == expectedSize)
            return;

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort; the size check is what matters.
        }

        throw new IOException("Download didn't finish. Try again.");
    }

    internal static string ReleaseTagUrl(string owner, string repo, string tag)
    {
        if (!SafeRepoPart.IsMatch(owner) || !SafeRepoPart.IsMatch(repo) || !SafeRepoPart.IsMatch(tag))
            throw new InvalidOperationException("Invalid GitHub repository.");

        return $"https://api.github.com/repos/{owner}/{repo}/releases/tags/{tag}";
    }

    internal static GitHubAsset? SelectAsset(
        IReadOnlyList<GitHubAsset> assets,
        Ue4ssChannel channel,
        string? pinnedGitSha = null,
        Ue4ssAssetStyle style = Ue4ssAssetStyle.Official)
    {
        IEnumerable<GitHubAsset> matches = channel == Ue4ssChannel.ZDev
            ? assets.Where(a => IsZDevZip(a.Name, style))
            : assets.Where(a => IsReleaseZip(a.Name, style));

        var pin = NormalizeGitSha(pinnedGitSha);
        if (pin is not null)
        {
            var needle = "-g" + pin;
            matches = matches.Where(a =>
                a.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        return matches
            .OrderByDescending(a => a.UpdatedAt)
            .ThenByDescending(a => a.CreatedAt)
            .FirstOrDefault();
    }

    internal static string? NormalizeGitSha(string? sha)
    {
        if (string.IsNullOrWhiteSpace(sha))
            return null;

        sha = sha.Trim();
        if (sha.StartsWith("g", StringComparison.OrdinalIgnoreCase) && sha.Length > 1)
            sha = sha[1..];

        return sha.ToLowerInvariant();
    }

    // e.g. UE4SS_v3.0.1-1028-gd7e7826d.zip — never zDEV- or helper zips.
    // Palworld: UE4SS-Palworld.zip
    internal static bool IsReleaseZip(string name, Ue4ssAssetStyle style = Ue4ssAssetStyle.Official)
        => style == Ue4ssAssetStyle.Palworld
            ? IsPalworldNamedZip(name, zDev: false)
            : name.StartsWith("UE4SS_v", StringComparison.OrdinalIgnoreCase)
              && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
              && !name.StartsWith("zDEV-", StringComparison.OrdinalIgnoreCase);

    // e.g. zDEV-UE4SS_v3.0.1-1028-gd7e7826d.zip
    // Palworld: UE4SS-Palworld_zDev.zip
    internal static bool IsZDevZip(string name, Ue4ssAssetStyle style = Ue4ssAssetStyle.Official)
        => style == Ue4ssAssetStyle.Palworld
            ? IsPalworldNamedZip(name, zDev: true)
            : name.StartsWith("zDEV-UE4SS_", StringComparison.OrdinalIgnoreCase)
              && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static bool IsPalworldNamedZip(string name, bool zDev)
    {
        var file = Path.GetFileName(name);
        if (!file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || !file.StartsWith("UE4SS-Palworld", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var isZDev = file.Contains("zdev", StringComparison.OrdinalIgnoreCase);
        return zDev == isZDev;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UE4SS-Installer-v1");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var token = ReadOptionalToken();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static string? ReadOptionalToken()
    {
        foreach (var name in new[] { "UE4SS_INSTALLER_GITHUB_TOKEN", "GITHUB_TOKEN" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static void EnsureApiSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden
            or System.Net.HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException(
                "GitHub's download listing limit was reached for this network. Wait and try again, or set UE4SS_INSTALLER_GITHUB_TOKEN. A GitHub account is not required for normal use.");
        }

        response.EnsureSuccessStatusCode();
    }

    private static void EnsureTrustedDownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("GitHub returned an unexpected download URL.");
        }

        var host = uri.Host;
        var trusted = host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                      || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
                      || host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                      || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

        if (!trusted)
            throw new InvalidOperationException("GitHub returned an unexpected download URL.");
    }
}

internal sealed class GitHubRelease
{
    public List<GitHubAsset> Assets { get; set; } = [];
}

internal sealed class GitHubAsset
{
    public string Name { get; set; } = "";

    public string BrowserDownloadUrl { get; set; } = "";

    public long Size { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
