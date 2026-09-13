using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Celeste.Mod.MiaoNet;

// TODO we should support retrying failed requests
public static class AvatarManager
{
    private const int HashLength = 16;

    private const string ImageExtension = ".png";
    private const string MetaExtension = ".meta";

    private const string LegacyStateFileName = ".json";

    private const string VersionFileName = "v2";

    private static readonly string PathAvatarCache;

    private static readonly HttpClient httpClient;

    private static readonly ConcurrentDictionary<Uri, Task<string>> runningFetches = new();

    static AvatarManager()
    {
        string? pathCache = Everest.Loader.PathCache ?? throw new InvalidOperationException(
            "AvatarManager was used before Everest finished initializing."
        );
        PathAvatarCache = Path.Combine(pathCache, "MiaoNet", "AvatarCache");

        httpClient = new();
        string ua = $"MiaoNet.Client/{MiaoNetModule.Instance.Metadata.VersionString}";
        httpClient.DefaultRequestHeaders.Add("User-Agent", ua);
        Logger.Info(LT.MiaoNetAvatar, $"Using User-Agent {ua}.");

        MigrateLegacyCache();
    }

    public static Task<string> GetAsync(Uri uri)
    {
    Retry:
        if (runningFetches.TryGetValue(uri, out Task<string>? running))
            return running;

        TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!runningFetches.TryAdd(uri, completion.Task))
            goto Retry;

        _ = FetchAndCompleteAsync(uri, completion);
        return completion.Task;
    }

    private static async Task FetchAndCompleteAsync(Uri uri, TaskCompletionSource<string> completion)
    {
        try
        {
            completion.SetResult(await FetchAsync(uri));
        }
        catch (Exception e)
        {
            completion.SetException(e);
        }
        finally
        {
            runningFetches.TryRemove(uri, out _);
        }
    }

    private static async Task<string> FetchAsync(Uri uri)
    {
        Directory.CreateDirectory(PathAvatarCache);

        string imagePath = Path.Combine(PathAvatarCache, HashUri(uri) + ImageExtension);
        string metaPath = imagePath + MetaExtension;

        if (!File.Exists(imagePath) || !CacheInfo.TryRead(metaPath, out CacheInfo cache))
            return await DownloadAsync(uri, imagePath, metaPath, null);

        if (cache.CacheControlMaxAge is TimeSpan maxAge && DateTimeOffset.UtcNow < cache.FetchTime + maxAge)
        {
            Logger.Debug(LT.MiaoNetAvatar, $"Using locally cached {uri}.");
            return imagePath;
        }

        CacheInfo? revalidate = cache.ETag is not null || cache.LastModified is not null ? cache : null;
        return await DownloadAsync(uri, imagePath, metaPath, revalidate);
    }

    private static async Task<string> DownloadAsync(Uri uri, string imagePath, string metaPath, CacheInfo? revalidate)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, uri);

        if (revalidate is CacheInfo cached)
        {
            if (cached.ETag is not null)
                request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
            if (cached.LastModified is not null)
                request.Headers.IfModifiedSince = cached.LastModified;

            Logger.Debug(LT.MiaoNetAvatar, $"Revalidating cached {uri}...");
        }
        else
        {
            Logger.Debug(LT.MiaoNetAvatar, $"No cache found, requesting {uri}...");
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request);

        DateTimeOffset fetchTime = DateTimeOffset.UtcNow;
        TimeSpan? maxAge = response.Headers.CacheControl?.MaxAge;
        DateTimeOffset? lastModified = response.Content.Headers.LastModified;
        string? eTag = response.Headers.ETag?.Tag;

        if (revalidate is not null && response.StatusCode == HttpStatusCode.NotModified)
        {
            Logger.Debug(LT.MiaoNetAvatar, $"Remote resource is not modified, using cached {uri}.");

            CacheInfo refreshed = new(
                fetchTime,
                maxAge ?? revalidate.Value.CacheControlMaxAge,
                lastModified ?? revalidate.Value.LastModified,
                eTag ?? revalidate.Value.ETag
            );
            refreshed.Write(metaPath);
            return imagePath;
        }

        response.EnsureSuccessStatusCode();

        byte[] data = await response.Content.ReadAsByteArrayAsync();
        WriteFileAtomic(imagePath, data);
        new CacheInfo(fetchTime, maxAge, lastModified, eTag).Write(metaPath);
        return imagePath;
    }

    private static string HashUri(Uri uri)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri), digest);
        return Convert.ToHexString(digest[..(HashLength / 2)]);
    }

    private static void WriteFileAtomic(string path, byte[] contents)
    {
        string tmpPath = path + ".tmp";
        File.WriteAllBytes(tmpPath, contents);
        File.Move(tmpPath, path, true);
    }

    private static void MigrateLegacyCache()
    {
        try
        {
            string legacyStatePath = Path.Combine(PathAvatarCache, LegacyStateFileName);
            if (!File.Exists(legacyStatePath))
                return;

            Logger.Info(LT.MiaoNetAvatar, "Migrating v1 avatar cache to v2...");

            if (TryReadLegacyCache(legacyStatePath, out Dictionary<string, LegacyCacheInfo> legacy))
            {
                foreach ((string uriText, LegacyCacheInfo info) in legacy)
                {
                    try
                    {
                        MigrateLegacyEntry(uriText, info);
                    }
                    catch (Exception e)
                    {
                        Logger.Warn(LT.MiaoNetAvatar, $"Failed to migrate cached avatar \"{uriText}\".");
                        Logger.LogDetailed(e);
                    }
                }
            }
            else
            {
                Logger.Warn(LT.MiaoNetAvatar, "Failed to read v1 avatar cache state, discarding it.");
            }

            RemoveLegacyCacheFiles();
            File.WriteAllBytes(Path.Combine(PathAvatarCache, VersionFileName), []);

            Logger.Info(LT.MiaoNetAvatar, "Migrated v1 avatar cache to v2.");
        }
        catch (Exception e)
        {
            Logger.Error(LT.MiaoNetAvatar, "Failed to migrate v1 avatar cache to v2.");
            Logger.LogDetailed(e);
        }
    }

    private static void MigrateLegacyEntry(string uriText, LegacyCacheInfo info)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri))
            return;

        string legacyName = Path.GetFileName(info.FileName);
        if (legacyName.Length == 0)
            return;

        string legacyImagePath = Path.Combine(PathAvatarCache, legacyName);
        if (!File.Exists(legacyImagePath))
            return;

        string imagePath = Path.Combine(PathAvatarCache, HashUri(uri) + ImageExtension);
        if (File.Exists(imagePath))
            File.Delete(legacyImagePath);
        else
            File.Move(legacyImagePath, imagePath);

        new CacheInfo(
            new DateTimeOffset(info.FetchTime),
            info.CacheControlMaxAge,
            info.LastModified,
            info.ETag
        ).Write(imagePath + MetaExtension);
    }

    private static bool TryReadLegacyCache(string path, out Dictionary<string, LegacyCacheInfo> legacy)
    {
        legacy = [];

        try
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("clone", out JsonElement clone))
                return false;

            legacy = clone.Deserialize<Dictionary<string, LegacyCacheInfo>>() ?? [];
            return true;
        }
        catch (Exception e)
        {
            Logger.Warn(LT.MiaoNetAvatar, $"Failed to read v1 avatar cache state \"{path}\".");
            Logger.LogDetailed(e);
            return false;
        }
    }

    private static void RemoveLegacyCacheFiles()
    {
        foreach (string path in Directory.EnumerateFiles(PathAvatarCache))
        {
            string name = Path.GetFileName(path);

            if (name != LegacyStateFileName &&
                name != LegacyStateFileName + ".tmp" &&
                !IsLegacyImageName(name))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception e)
            {
                Logger.Warn(LT.MiaoNetAvatar, $"Failed to remove legacy avatar cache file \"{path}\".");
                Logger.LogDetailed(e);
            }
        }
    }

    private static bool IsLegacyImageName(string name)
    {
        if (!name.EndsWith(ImageExtension, StringComparison.Ordinal))
            return false;

        string baseName = name[..^ImageExtension.Length];
        if (baseName.Length is 0 or HashLength)
            return false;

        return baseName.All(static c => c is >= '0' and <= '9');
    }

    private sealed class LegacyCacheInfo
    {
        public DateTime FetchTime { get; set; }
        public string FileName { get; set; } = string.Empty;
        public TimeSpan? CacheControlMaxAge { get; set; }
        public DateTimeOffset? LastModified { get; set; }
        public string? ETag { get; set; }
    }

    private readonly record struct CacheInfo(
        DateTimeOffset FetchTime,
        TimeSpan? CacheControlMaxAge,
        DateTimeOffset? LastModified,
        string? ETag
    )
    {
        private const int LineCount = 4;

        public static bool TryRead(string path, out CacheInfo cache)
        {
            cache = default;

            try
            {
                if (!File.Exists(path))
                    return false;

                string[] lines = File.ReadAllLines(path);
                if (lines.Length != LineCount)
                    return false;

                if (!TryParseDateTimeOffset(lines[0], out DateTimeOffset fetchTime))
                    return false;

                TimeSpan? maxAge = TimeSpan.TryParse(lines[1], CultureInfo.InvariantCulture, out TimeSpan parsedMaxAge)
                    ? parsedMaxAge
                    : null;
                DateTimeOffset? lastModified = TryParseDateTimeOffset(lines[2], out DateTimeOffset parsedLastModified)
                    ? parsedLastModified
                    : null;

                cache = new(fetchTime, maxAge, lastModified, lines[3].Length == 0 ? null : lines[3]);
                return true;
            }
            catch (Exception e)
            {
                Logger.Warn(LT.MiaoNetAvatar, $"Failed to read avatar cache meta \"{path}\".");
                Logger.LogDetailed(e);
                return false;
            }
        }

        public void Write(string path)
        {
            string content = string.Join('\n',
                FetchTime.ToString("O", CultureInfo.InvariantCulture),
                CacheControlMaxAge?.ToString("c", CultureInfo.InvariantCulture),
                LastModified?.ToString("O", CultureInfo.InvariantCulture),
                ETag
            ) + '\n';
            WriteFileAtomic(path, Encoding.UTF8.GetBytes(content));
        }

        private static bool TryParseDateTimeOffset(string value, out DateTimeOffset result)
            => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }
}
