using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PackLauncher.Services;

public sealed record LauncherRelease(Version Version, string Tag, string DownloadUrl, long Size);

public sealed class LauncherUpdater
{
    private static readonly Regex VersionTag = new(@"^[vV]?\d+(\.\d+){0,3}$", RegexOptions.Compiled);

    private readonly HttpClient _http;

    public LauncherUpdater(HttpClient http) => _http = http;

    public async Task<LauncherRelease?> CheckAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AppConfig.ReleasesApiUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        LauncherRelease? best = null;

        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                continue;
            if (release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
                continue;

            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!VersionTag.IsMatch(tag) || !Version.TryParse(tag.TrimStart('v', 'V'), out var parsed))
                continue;
            var version = Normalize(parsed);

            string? url = null;
            long size = 0;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;
                url = asset.GetProperty("browser_download_url").GetString();
                size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                if (name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                    break;
            }

            if (url is null)
                continue;
            if (best is null || version > best.Version)
                best = new LauncherRelease(version, tag, url, size);
        }

        if (best is null || best.Version <= Normalize(AppPaths.CurrentVersion))
            return null;
        return best;
    }

    public async Task DownloadAndInstallAsync(LauncherRelease release, IProgress<double> progress, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{AppConfig.DataFolderName}-update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{AppConfig.DataFolderName}-Setup-{release.Version.ToString(3)}.exe");
        var tmp = path + ".download";

        using (var response = await _http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.Size;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(tmp);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0)
                    progress.Report(Math.Min(1, (double)done / total));
            }
        }

        File.Move(tmp, path, overwrite: true);

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
            UseShellExecute = true
        });

        System.Windows.Application.Current.Shutdown();
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
