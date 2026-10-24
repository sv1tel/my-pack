using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PackLauncher.Services;

public sealed class PackManifest
{
    public string Name { get; set; } = AppConfig.LauncherName;

    public string Version { get; set; } = "0";

    public string Minecraft { get; set; } = "1.21.1";

    public string? NeoForge { get; set; }

    public List<string> StrictFolders { get; set; } = ["mods", "resourcepacks", "shaderpacks"];

    public List<string> DefaultResourcePacks { get; set; } = [];

    public string? DefaultShaderPack { get; set; }

    public List<ExternalFile> External { get; set; } = [];

    [JsonIgnore]
    public string Commit { get; set; } = "";

    [JsonIgnore]
    public List<PackFile> Files { get; set; } = [];
}

public sealed class ExternalFile
{
    public string Path { get; set; } = "";

    public string Url { get; set; } = "";

    public string Sha1 { get; set; } = "";

    public long Size { get; set; }
}

public sealed class PackFile
{
    public required string Path { get; init; }

    public required string Hash { get; init; }

    public required bool IsGitBlob { get; init; }

    public required string Url { get; init; }

    public long Size { get; init; }
}

public sealed class UpdatePlan
{
    public required PackManifest Manifest { get; init; }

    public List<PackFile> ToDownload { get; } = [];

    public List<string> ToDelete { get; } = [];

    public List<PackFile> ToRecord { get; } = [];

    public bool IsUpToDate => ToDownload.Count == 0 && ToDelete.Count == 0;

    public bool HasWork => !IsUpToDate || ToRecord.Count > 0;

    public long DownloadBytes => ToDownload.Sum(f => f.Size);
}

public sealed class ModpackUpdater
{
    private const string StateFileName = ".pack-state.json";
    private const string CachedManifestFileName = ".pack-manifest.json";
    private static readonly string[] ServiceFiles = ["pack.json", "manifest.json"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions OptionsTxtJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HttpClient _http;

    public ModpackUpdater(HttpClient http) => _http = http;

    public async Task<PackManifest> FetchManifestAsync(CancellationToken ct = default)
    {
        string commit;
        using (var request = new HttpRequestMessage(HttpMethod.Get, AppConfig.CommitApiUrl))
        {
            request.Headers.Accept.ParseAdd("application/vnd.github.sha");
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            commit = (await response.Content.ReadAsStringAsync(ct)).Trim();
        }

        var packJson = await _http.GetStringAsync(AppConfig.RawFileUrl(commit, "pack.json"), ct);
        var manifest = JsonSerializer.Deserialize<PackManifest>(packJson, JsonOptions)
                       ?? throw new InvalidDataException("pack.json");
        manifest.Commit = commit;

        using (var request = new HttpRequestMessage(HttpMethod.Get, AppConfig.TreeApiUrl(commit)))
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("truncated", out var truncated) && truncated.GetBoolean())
                throw new InvalidDataException(Loc.T("err_tree_truncated"));

            var prefix = AppConfig.PackFolderInRepo.Trim('/') + "/";
            foreach (var entry in doc.RootElement.GetProperty("tree").EnumerateArray())
            {
                if (entry.GetProperty("type").GetString() != "blob")
                    continue;
                var path = entry.GetProperty("path").GetString() ?? "";
                if (!path.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                var rel = path[prefix.Length..];
                if (ServiceFiles.Contains(rel, StringComparer.OrdinalIgnoreCase) || rel.Split('/').Any(p => p.StartsWith('.')))
                    continue;
                rel = NormalizeRelative(rel);
                var size = entry.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0;
                manifest.Files.Add(new PackFile
                {
                    Path = rel,
                    Hash = entry.GetProperty("sha").GetString() ?? "",
                    IsGitBlob = true,
                    Url = AppConfig.RawFileUrl(commit, rel),
                    Size = size
                });
            }
        }

        foreach (var ext in manifest.External.Where(e => !string.IsNullOrWhiteSpace(e.Path) && !string.IsNullOrWhiteSpace(e.Url)))
        {
            var rel = NormalizeRelative(ext.Path);
            manifest.Files.RemoveAll(f => string.Equals(f.Path, rel, StringComparison.OrdinalIgnoreCase));
            manifest.Files.Add(new PackFile
            {
                Path = rel,
                Hash = ext.Sha1.Trim().ToLowerInvariant(),
                IsGitBlob = false,
                Url = ext.Url,
                Size = ext.Size
            });
        }

        return manifest;
    }

    public Task<UpdatePlan> PlanAsync(PackManifest manifest, string gameDir, bool fullCheck, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var plan = new UpdatePlan { Manifest = manifest };
            var state = LoadState(gameDir);
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in manifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                wanted.Add(file.Path);
                var full = Path.Combine(gameDir, file.Path);

                if (!File.Exists(full))
                {
                    plan.ToDownload.Add(file);
                    continue;
                }

                state.Files.TryGetValue(file.Path, out var record);
                var authorChanged = record is null || !string.Equals(record.Hash, file.Hash, StringComparison.OrdinalIgnoreCase);
                var strict = IsInStrictFolder(file.Path, manifest);

                if (strict || fullCheck)
                {
                    if (!authorChanged && !fullCheck && RecordMatches(record!, full))
                        continue;
                    if (HashMatches(full, file))
                        plan.ToRecord.Add(file);
                    else
                        plan.ToDownload.Add(file);
                }
                else if (authorChanged)
                {
                    if (HashMatches(full, file))
                        plan.ToRecord.Add(file);
                    else
                        plan.ToDownload.Add(file);
                }
            }

            foreach (var old in state.Files.Keys)
            {
                if (!wanted.Contains(old) && File.Exists(Path.Combine(gameDir, old)))
                    plan.ToDelete.Add(old);
            }

            foreach (var folder in manifest.StrictFolders)
            {
                var dir = Path.Combine(gameDir, NormalizeRelative(folder));
                if (!Directory.Exists(dir))
                    continue;

                foreach (var full in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(gameDir, full).Replace('\\', '/');
                    if (rel.EndsWith(".download", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!wanted.Contains(rel) && !plan.ToDelete.Contains(rel, StringComparer.OrdinalIgnoreCase))
                        plan.ToDelete.Add(rel);
                }
            }

            return plan;
        }, ct);

    public async Task ApplyAsync(UpdatePlan plan, string gameDir, IProgress<(string Text, double Fraction)> progress,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(gameDir);
        var state = LoadState(gameDir);

        foreach (var rel in plan.ToDelete)
        {
            try
            {
                var full = Path.Combine(gameDir, rel);
                if (File.Exists(full))
                    File.Delete(full);
                state.Files.Remove(rel);
            }
            catch
            {
            }
        }

        foreach (var file in plan.ToRecord)
            state.Files[file.Path] = MakeRecord(file, Path.Combine(gameDir, file.Path));

        var total = Math.Max(1, plan.DownloadBytes);
        long done = 0;
        var buffer = new byte[81920];

        for (var i = 0; i < plan.ToDownload.Count; i++)
        {
            var file = plan.ToDownload[i];
            var full = Path.Combine(gameDir, file.Path);
            var tmp = full + ".download";
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            var label = Loc.F("status_downloading", i + 1, plan.ToDownload.Count, Path.GetFileName(file.Path));
            progress.Report((label, (double)done / total));

            var startDone = done;
            Exception? last = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                done = startDone;
                try
                {
                    using (var response = await _http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, ct))
                    {
                        response.EnsureSuccessStatusCode();
                        await using var input = await response.Content.ReadAsStreamAsync(ct);
                        await using var output = File.Create(tmp);
                        int read;
                        while ((read = await input.ReadAsync(buffer, ct)) > 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, read), ct);
                            done += read;
                            progress.Report((label, Math.Min(1, (double)done / total)));
                        }
                    }

                    if (!HashMatches(tmp, file))
                        throw new InvalidDataException(Loc.F("err_hash", file.Path));
                    last = null;
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    last = ex;
                    TryDelete(tmp);
                    await Task.Delay(800 * (attempt + 1), ct);
                }
            }

            if (last is not null)
            {
                SaveState(gameDir, state);
                throw last;
            }

            File.Move(tmp, full, overwrite: true);
            state.Files[file.Path] = MakeRecord(file, full);
        }

        state.Version = plan.Manifest.Version;
        state.Commit = plan.Manifest.Commit;
        SaveState(gameDir, state);
        File.WriteAllText(Path.Combine(gameDir, CachedManifestFileName), JsonSerializer.Serialize(plan.Manifest, JsonOptions));
        progress.Report((Loc.T("status_done"), 1));
    }

    public static PackManifest? LoadCachedManifest(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, CachedManifestFileName);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<PackManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
        }
        return null;
    }

    public static string? GetInstalledVersion(string gameDir) => LoadState(gameDir).Version;

    public static void ApplyGameDefaults(PackManifest manifest, string gameDir)
    {
        var state = LoadState(gameDir);

        var packs = (manifest.DefaultResourcePacks ?? new List<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();
        if (packs.Count == 0)
        {
            packs = state.Files.Keys
                .Where(k => k.StartsWith("resourcepacks/", StringComparison.OrdinalIgnoreCase))
                .Select(k => k.Split('/')[1])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (packs.Count > 0)
            EnableResourcePacks(gameDir, packs, manifest.Minecraft);

        var shader = manifest.DefaultShaderPack?.Trim();
        if (!string.IsNullOrEmpty(shader) && state.AppliedShader != shader)
        {
            if (EnableShaderPack(gameDir, shader))
            {
                state.AppliedShader = shader;
                SaveState(gameDir, state);
            }
        }
    }

    private static void EnableResourcePacks(string gameDir, List<string> packs, string minecraft)
    {
        var optionsPath = Path.Combine(gameDir, "options.txt");
        var exists = File.Exists(optionsPath);
        var lines = exists ? File.ReadAllLines(optionsPath).ToList() : new List<string>();

        var selected = ReadJsonList(lines, "resourcePacks:") ?? new List<string> { "vanilla", "mod_resources" };
        var incompatible = ReadJsonList(lines, "incompatibleResourcePacks:") ?? new List<string>();
        if (!selected.Contains("vanilla"))
            selected.Insert(0, "vanilla");
        if (!selected.Contains("mod_resources"))
            selected.Insert(Math.Min(1, selected.Count), "mod_resources");

        foreach (var name in packs.AsEnumerable().Reverse())
        {
            var file = Path.Combine(gameDir, "resourcepacks", name);
            if (!File.Exists(file) && !Directory.Exists(file))
                continue;

            var id = "file/" + name;
            selected.Remove(id);
            selected.Add(id);
            if (!incompatible.Contains(id))
                incompatible.Add(id);
        }

        if (!exists && minecraft == "1.21.1")
            lines.Add("version:3955");
        WriteJsonList(lines, "resourcePacks:", selected);
        WriteJsonList(lines, "incompatibleResourcePacks:", incompatible);
        if (!exists && Loc.I.Language is "ru" or "uk")
            lines.Add(Loc.I.Language == "uk" ? "lang:uk_ua" : "lang:ru_ru");
        Directory.CreateDirectory(gameDir);
        File.WriteAllLines(optionsPath, lines);
    }

    private static List<string>? ReadJsonList(List<string> lines, string key)
    {
        var line = lines.FirstOrDefault(l => l.StartsWith(key, StringComparison.Ordinal));
        if (line is null)
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(line[key.Length..]);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteJsonList(List<string> lines, string key, List<string> values)
    {
        var newLine = key + JsonSerializer.Serialize(values, OptionsTxtJson);
        var index = lines.FindIndex(l => l.StartsWith(key, StringComparison.Ordinal));
        if (index >= 0)
            lines[index] = newLine;
        else
            lines.Add(newLine);
    }

    private static bool EnableShaderPack(string gameDir, string shader)
    {
        var file = Path.Combine(gameDir, "shaderpacks", shader);
        if (!File.Exists(file) && !Directory.Exists(file))
            return false;

        var configPath = Path.Combine(gameDir, "config", "iris.properties");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var lines = File.Exists(configPath) ? File.ReadAllLines(configPath, Encoding.Latin1).ToList() : new List<string>();

        SetProperty(lines, "shaderPack", EscapeProperty(shader));
        SetProperty(lines, "enableShaders", "true");
        File.WriteAllLines(configPath, lines, Encoding.Latin1);
        return true;
    }

    private static void SetProperty(List<string> lines, string key, string value)
    {
        var index = lines.FindIndex(l => l.TrimStart().StartsWith(key + "=", StringComparison.Ordinal));
        if (index >= 0)
            lines[index] = $"{key}={value}";
        else
            lines.Add($"{key}={value}");
    }

    private static string EscapeProperty(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value)
        {
            if (c == '\\')
                sb.Append("\\\\");
            else if (c > 0x7E || c < 0x20)
                sb.Append($"\\u{(int)c:x4}");
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool IsInStrictFolder(string rel, PackManifest manifest) =>
        manifest.StrictFolders.Any(f =>
            rel.StartsWith(NormalizeRelative(f).TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));

    private static bool HashMatches(string fullPath, PackFile file)
    {
        if (string.IsNullOrEmpty(file.Hash))
            return true;
        return string.Equals(ComputeHash(fullPath, file.IsGitBlob), file.Hash, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeHash(string fullPath, bool gitBlob)
    {
        using var stream = File.OpenRead(fullPath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        if (gitBlob)
            hash.AppendData(Encoding.ASCII.GetBytes($"blob {stream.Length}\0"));
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static bool RecordMatches(FileRecord record, string fullPath)
    {
        var info = new FileInfo(fullPath);
        return info.Exists && info.Length == record.Size && info.LastWriteTimeUtc.Ticks == record.Ticks;
    }

    private static FileRecord MakeRecord(PackFile file, string fullPath)
    {
        var info = new FileInfo(fullPath);
        return new FileRecord
        {
            Hash = file.Hash,
            Size = info.Exists ? info.Length : 0,
            Ticks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static string NormalizeRelative(string path)
    {
        var rel = path.Replace('\\', '/').TrimStart('/');
        if (rel.Length == 0 || Path.IsPathRooted(rel) || rel.Split('/').Any(p => p is ".." or "." or ""))
            throw new InvalidDataException(Loc.F("err_bad_path", path));
        return rel;
    }

    private sealed class FileRecord
    {
        public string Hash { get; set; } = "";

        public long Size { get; set; }

        public long Ticks { get; set; }
    }

    private sealed class PackState
    {
        public string? Version { get; set; }

        public string? Commit { get; set; }

        public string? AppliedShader { get; set; }

        public Dictionary<string, FileRecord> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static PackState LoadState(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, StateFileName);
            if (File.Exists(path))
            {
                var state = JsonSerializer.Deserialize<PackState>(File.ReadAllText(path), JsonOptions) ?? new PackState();
                state.Files = new Dictionary<string, FileRecord>(state.Files ?? new Dictionary<string, FileRecord>(), StringComparer.OrdinalIgnoreCase);
                return state;
            }
        }
        catch
        {
        }
        return new PackState();
    }

    private static void SaveState(string gameDir, PackState state)
    {
        Directory.CreateDirectory(gameDir);
        var path = Path.Combine(gameDir, StateFileName);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(state, JsonOptions));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
