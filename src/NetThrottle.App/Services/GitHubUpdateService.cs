using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetThrottle.App.Services;

public interface IUpdateService
{
    /// <summary>True when this build can self-install an update (installed, not portable).</summary>
    bool CanSelfInstall { get; }

    /// <summary>Queries GitHub for the latest release and compares it to the running version.</summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);

    /// <summary>Downloads the Setup.exe asset to a temp file and returns its path.
    /// Progress is reported as a fraction 0–1.</summary>
    Task<string> DownloadAsync(UpdateCheckResult update, IProgress<double>? progress, CancellationToken ct = default);

    /// <summary>Launches the downloaded installer elevated. The caller should exit afterwards.</summary>
    void RunInstaller(string path);

    /// <summary>Opens the release page in the default browser (portable fallback).</summary>
    void OpenReleasePage(UpdateCheckResult update);
}

/// <summary>
/// Update service that uses the public GitHub Releases API. The installed build
/// downloads the NSIS Setup.exe asset and relaunches it elevated; the portable
/// build only notifies and links to the download page.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string Owner = "akon47";
    private const string Repo = "NetThrottle";
    private static readonly Uri LatestReleaseUri =
        new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

    private readonly HttpClient _http;

    public GitHubUpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub rejects requests without a User-Agent.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{Repo}-Updater");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public bool CanSelfInstall => !SettingsPaths.IsPortable;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        Version current = CurrentVersion();

        string json = await _http.GetStringAsync(LatestReleaseUri, ct).ConfigureAwait(false);
        ReleaseDto? release = JsonSerializer.Deserialize<ReleaseDto>(json);
        if (release?.TagName is null || !TryParseTag(release.TagName, out Version? latest))
            return UpdateCheckResult.UpToDate(current);

        AssetDto? setup = release.Assets?
            .FirstOrDefault(a => a.Name?.EndsWith("_Setup.exe", StringComparison.OrdinalIgnoreCase) == true);

        return new UpdateCheckResult
        {
            IsUpdateAvailable = latest > current,
            CurrentVersion = current,
            LatestVersion = latest,
            Tag = release.TagName,
            ReleaseNotes = release.Body,
            ReleasePageUrl = release.HtmlUrl,
            SetupAssetUrl = setup?.BrowserDownloadUrl,
            SetupAssetName = setup?.Name,
        };
    }

    public async Task<string> DownloadAsync(UpdateCheckResult update, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (update.SetupAssetUrl is null)
            throw new InvalidOperationException("The release has no installer asset.");

        string fileName = update.SetupAssetName ?? $"NetThrottle_{update.Tag}_Setup.exe";
        string target = Path.Combine(Path.GetTempPath(), fileName);
        await DownloadToFileAsync(update.SetupAssetUrl, target, progress, ct).ConfigureAwait(false);
        return target;
    }

    public void RunInstaller(string path)
    {
        // Elevated; the NSIS script waits for this app to exit (mutex handshake)
        // and stops the WinDivert driver before replacing files.
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = "runas",
        });
    }

    public void OpenReleasePage(UpdateCheckResult update)
    {
        string url = update.ReleasePageUrl ?? $"https://github.com/{Owner}/{Repo}/releases/latest";
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private async Task DownloadToFileAsync(string url, string path, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var destination = File.Create(path);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            if (total is > 0)
                progress?.Report((double)read / total.Value);
        }
    }

    private static Version CurrentVersion() => Normalize(
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    private static bool TryParseTag(string tag, out Version version)
    {
        string trimmed = tag.TrimStart('v', 'V');
        if (Version.TryParse(trimmed, out Version? parsed))
        {
            version = Normalize(parsed);
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    /// <summary>Collapse to Major.Minor.Build so 3- and 4-part versions compare correctly.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private sealed record ReleaseDto(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] List<AssetDto>? Assets);

    private sealed record AssetDto(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl);
}
