using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UE4SSInstaller.Services;

public enum Ue4ssChannel
{
    Release,
    ZDev
}

/// <summary>
/// Downloads UE4SS from the <c>experimental-latest</c> GitHub release only.
/// That tag keeps historical assets, so we pick the newest matching zip by date.
/// </summary>
public static class GitHubFetcher
{
    private const string ExperimentalLatestUrl = "https://api.github.com/repos/UE4SS-RE/RE-UE4SS/releases/tags/experimental-latest";

    private static readonly Regex SafeRepoPart = new(
        @"^[A-Za-z0-9_.-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HttpClient Http = CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task<string> DownloadAsync(Ue4ssChannel channel, CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(ExperimentalLatestUrl, cancellationToken);
        EnsureApiSuccess(response);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken)
                      ?? throw new InvalidOperationException("GitHub returned an empty release payload.");

        var asset = SelectAsset(release.Assets, channel)
                    ?? throw new InvalidOperationException($"No matching {channel} zip was found on experimental-latest.");

        return await DownloadAssetAsync(asset, cancellationToken);
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

        return destination;
    }

    internal static GitHubAsset? SelectAsset(IReadOnlyList<GitHubAsset> assets, Ue4ssChannel channel)
    {
        IEnumerable<GitHubAsset> matches = channel == Ue4ssChannel.ZDev
            ? assets.Where(a => IsZDevZip(a.Name))
            : assets.Where(a => IsReleaseZip(a.Name));

        return matches
            .OrderByDescending(a => a.UpdatedAt)
            .ThenByDescending(a => a.CreatedAt)
            .FirstOrDefault();
    }

    // e.g. UE4SS_v3.0.1-1028-gd7e7826d.zip — never zDEV- or helper zips.
    private static bool IsReleaseZip(string name)
        => name.StartsWith("UE4SS_v", StringComparison.OrdinalIgnoreCase)
           && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
           && !name.StartsWith("zDEV-", StringComparison.OrdinalIgnoreCase);

    // e.g. zDEV-UE4SS_v3.0.1-1028-gd7e7826d.zip
    private static bool IsZDevZip(string name)
        => name.StartsWith("zDEV-UE4SS_", StringComparison.OrdinalIgnoreCase)
           && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

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

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
