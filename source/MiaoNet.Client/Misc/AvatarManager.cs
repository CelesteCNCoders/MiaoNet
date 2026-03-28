using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Celeste.Mod.MiaoNet;

public static class AvatarManager
{
    private static readonly string PathAvatarCache =
        Path.Combine(Everest.Loader.PathCache, "MiaoNet", "AvatarCache");

    private static int nextID;

    private struct CacheInfo
    {
        public DateTime FetchTime { get; set; }
        public string FileName { get; set; }

        public TimeSpan? CacheControlMaxAge { get; set; }
        public DateTimeOffset? LastModified { get; set; }
        public string? ETag { get; set; }
    }

    private static readonly HttpClient httpClient;
    private static readonly ConcurrentDictionary<Uri, CacheInfo> memoryCache;

    static AvatarManager()
    {
        httpClient = new();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "MiaoNet Client Avatar Http Client");
        memoryCache = new();

        try
        {
            string file = Path.Combine(PathAvatarCache, ".json");
            if (File.Exists(file))
            {
                var node = JsonSerializer.Deserialize<JsonNode>(File.ReadAllText(file));
                int? id = node?["id"]?.GetValue<int>();
                var dicNode = node?["clone"];
                var dic = JsonSerializer.Deserialize<ConcurrentDictionary<Uri, CacheInfo>>(dicNode);

                if (id is null || dic is null)
                {
                    Logger.Error(LT.MiaoNetAvatar, "Failed to read broken cache state.");
                    return;
                }
                nextID = id.Value;
                memoryCache = dic;
            }
        }
        catch (Exception e)
        {
            Logger.Error(LT.MiaoNetAvatar, "Failed to read cache state from disk.");
            Logger.LogDetailed(e);
        }
    }

    public static void PersistStateToDisk()
    {
        var clone = memoryCache.ToDictionary();
        var id = nextID;
        JsonObject obj = new()
        {
            ["id"] = id,
            ["clone"] = JsonSerializer.SerializeToNode(clone)
        };
        var str = JsonSerializer.Serialize(obj);
        Directory.CreateDirectory(PathAvatarCache);
        string tmp = Path.Combine(PathAvatarCache, ".json.tmp");
        string real = Path.Combine(PathAvatarCache, ".json");
        File.WriteAllText(tmp, str);
        File.Move(tmp, real, true);
    }

    public static async ValueTask<string> GetAsync(Uri uri)
    {
        if (memoryCache.TryGetValue(uri, out var cache) && File.Exists(Path.Combine(PathAvatarCache, cache.FileName)))
        {
            if (cache.CacheControlMaxAge is TimeSpan timeSpan && DateTime.UtcNow < cache.FetchTime + timeSpan)
            {
                Logger.Debug(LT.MiaoNetAvatar, $"Using locally cached {uri}.");
                return Path.Combine(PathAvatarCache, cache.FileName);
            }

            if (cache.ETag is not null || cache.LastModified is not null)
            {
                HttpRequestMessage req = new(HttpMethod.Get, uri);

                if (cache.ETag is not null)
                    req.Headers.IfNoneMatch.Add(new(cache.ETag));
                if (cache.LastModified is not null)
                    req.Headers.IfModifiedSince = cache.LastModified;

                var res = await httpClient.SendAsync(req);

                cache.FetchTime = DateTime.UtcNow;
                cache.CacheControlMaxAge = res.Headers.CacheControl?.MaxAge;
                cache.LastModified = res.Content.Headers.LastModified;
                cache.ETag = res.Headers.ETag?.Tag;

                if (res.StatusCode == HttpStatusCode.NotModified)
                {
                    Logger.Debug(LT.MiaoNetAvatar, $"Remote resource is not modified, using cached {uri}.");
                    memoryCache[uri] = cache;
                    return Path.Combine(PathAvatarCache, cache.FileName);
                }
                else
                {
                    Logger.Debug(LT.MiaoNetAvatar, $"Remote resource is modified, requesting {uri}...");
                    res.EnsureSuccessStatusCode();
                    await FetchAndSave(res, cache.FileName);
                    memoryCache[uri] = cache;
                    return Path.Combine(PathAvatarCache, cache.FileName);
                }
            }
            goto FullFetch;
        }
    FullFetch:
        {
            Logger.Debug(LT.MiaoNetAvatar, $"No cache found, requesting {uri}...");
            var res = await httpClient.GetAsync(uri);

            var cacheControlMaxAge = res.Headers.CacheControl?.MaxAge;
            var lastModified = res.Content.Headers.LastModified;
            var eTag = res.Headers.ETag?.Tag;

            string fileName = $"{Interlocked.Increment(ref nextID)}.png";
            await FetchAndSave(res, fileName);

            memoryCache.AddOrUpdate(uri, new CacheInfo()
            {
                FetchTime = DateTime.UtcNow,
                FileName = fileName,
                CacheControlMaxAge = cacheControlMaxAge,
                LastModified = lastModified,
                ETag = eTag
            }, (u, o) => o);

            return Path.Combine(PathAvatarCache, fileName);
        }
    }

    private static async Task FetchAndSave(HttpResponseMessage message, string fileName)
    {
        var arr = await message.Content.ReadAsByteArrayAsync();

        Directory.CreateDirectory(PathAvatarCache);
        string pathToAvatarCacheFile = Path.Combine(PathAvatarCache, fileName);
        await File.WriteAllBytesAsync(pathToAvatarCacheFile, arr);
    }
}
