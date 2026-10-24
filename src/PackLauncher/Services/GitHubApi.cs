using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PackLauncher.Services;

public sealed class GitHubRateLimitException : Exception
{
    public GitHubRateLimitException(DateTimeOffset? resetAt)
        : base(resetAt is null
            ? Loc.T("err_rate_limit")
            : Loc.F("err_rate_limit_until", resetAt.Value.ToLocalTime().ToString("HH:mm")))
    {
        ResetAt = resetAt;
    }

    public DateTimeOffset? ResetAt { get; }
}

public sealed class GitHubApi
{
    private static readonly string CacheFile = Path.Combine(AppPaths.DataDir, "github-cache.json");
    private static readonly SemaphoreSlim Lock = new(1, 1);

    private readonly HttpClient _http;
    private Dictionary<string, CacheEntry>? _cache;

    public GitHubApi(HttpClient http) => _http = http;

    public async Task<string?> GetAsync(string url, string accept, CancellationToken ct = default)
    {
        var key = accept + " " + url;
        await Lock.WaitAsync(ct);
        try
        {
            var cache = LoadCache();
            cache.TryGetValue(key, out var cached);

            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.ParseAdd(accept);
                if (cached?.ETag is { Length: > 0 } etag)
                    request.Headers.TryAddWithoutValidation("If-None-Match", etag);
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException) when (cached is not null && IsFresh(cached))
            {
                return cached.Body;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
                {
                    cached.Checked = DateTimeOffset.UtcNow;
                    SaveCache(cache);
                    return cached.Body;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                {
                    if (cached is not null)
                        return cached.Body;
                    throw new GitHubRateLimitException(ReadReset(response));
                }

                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync(ct);
                cache[key] = new CacheEntry
                {
                    ETag = response.Headers.ETag?.ToString(),
                    Body = body,
                    Checked = DateTimeOffset.UtcNow
                };
                SaveCache(cache);
                return body;
            }
        }
        finally
        {
            Lock.Release();
        }
    }

    private static bool IsFresh(CacheEntry entry) => DateTimeOffset.UtcNow - entry.Checked < TimeSpan.FromDays(30);

    private static DateTimeOffset? ReadReset(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
            long.TryParse(values.FirstOrDefault(), out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        return null;
    }

    private Dictionary<string, CacheEntry> LoadCache()
    {
        if (_cache is not null)
            return _cache;
        try
        {
            if (File.Exists(CacheFile))
                _cache = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(CacheFile));
        }
        catch
        {
            _cache = null;
        }
        _cache ??= new Dictionary<string, CacheEntry>();
        return _cache;
    }

    private static void SaveCache(Dictionary<string, CacheEntry> cache)
    {
        try
        {
            var limit = DateTimeOffset.UtcNow - TimeSpan.FromDays(30);
            foreach (var old in cache.Where(p => p.Value.Checked < limit).Select(p => p.Key).ToList())
                cache.Remove(old);

            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(CacheFile + ".tmp", JsonSerializer.Serialize(cache));
            File.Move(CacheFile + ".tmp", CacheFile, overwrite: true);
        }
        catch
        {
        }
    }

    public sealed class CacheEntry
    {
        public string? ETag { get; set; }

        public string Body { get; set; } = "";

        public DateTimeOffset Checked { get; set; }
    }
}
