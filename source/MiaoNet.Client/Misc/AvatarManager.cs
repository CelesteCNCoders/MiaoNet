using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
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
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = ConnectPublicHostAsync
        };
        httpClient = new(handler) { Timeout = TimeSpan.FromSeconds(20) };
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
        if (!AvatarDownloadPolicy.IsAllowedUri(uri))
            throw new InvalidDataException($"Avatar URL is not an allowed HTTPS URL: {uri}");

        if (memoryCache.TryGetValue(uri, out var cache) && File.Exists(Path.Combine(PathAvatarCache, cache.FileName)))
        {
            if (cache.CacheControlMaxAge is TimeSpan timeSpan && DateTime.UtcNow < cache.FetchTime + timeSpan)
            {
                Logger.Debug(LT.MiaoNetAvatar, $"Using locally cached {uri}.");
                return Path.Combine(PathAvatarCache, cache.FileName);
            }

            if (cache.ETag is not null || cache.LastModified is not null)
            {
                using var res = await SendAsync(uri, cache);

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
            using var res = await SendAsync(uri, null);
            res.EnsureSuccessStatusCode();

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
        if (message.Content.Headers.ContentLength > AvatarDownloadPolicy.MaxDownloadBytes)
            throw new InvalidDataException("Avatar is larger than the allowed download size.");

        await using Stream source = await message.Content.ReadAsStreamAsync();
        using MemoryStream destination = new();
        byte[] buffer = new byte[16 * 1024];
        int total = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer);
            if (read == 0)
                break;
            total = checked(total + read);
            if (total > AvatarDownloadPolicy.MaxDownloadBytes)
                throw new InvalidDataException("Avatar is larger than the allowed download size.");
            destination.Write(buffer, 0, read);
        }

        byte[] arr = destination.ToArray();
        if (!AvatarDownloadPolicy.IsSupportedImage(arr))
            throw new InvalidDataException("Avatar is not a supported image or exceeds the dimension limit.");

        Directory.CreateDirectory(PathAvatarCache);
        string pathToAvatarCacheFile = Path.Combine(PathAvatarCache, fileName);
        string temporaryPath = pathToAvatarCacheFile + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, arr);
        File.Move(temporaryPath, pathToAvatarCacheFile, true);
    }

    private static async Task<HttpResponseMessage> SendAsync(Uri initialUri, CacheInfo? cache)
    {
        Uri uri = initialUri;
        for (int redirects = 0; ; redirects++)
        {
            if (!AvatarDownloadPolicy.IsAllowedUri(uri))
                throw new InvalidDataException($"Avatar redirect is not an allowed HTTPS URL: {uri}");

            using HttpRequestMessage request = new(HttpMethod.Get, uri);
            if (cache is CacheInfo cacheInfo)
            {
                if (cacheInfo.ETag is not null)
                    request.Headers.IfNoneMatch.Add(new(cacheInfo.ETag));
                if (cacheInfo.LastModified is not null)
                    request.Headers.IfModifiedSince = cacheInfo.LastModified;
            }

            HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!IsRedirect(response.StatusCode))
                return response;

            Uri? location = response.Headers.Location;
            response.Dispose();
            if (location is null || redirects >= AvatarDownloadPolicy.MaxRedirects)
                throw new InvalidDataException("Avatar download has an invalid or excessive redirect chain.");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static async ValueTask<Stream> ConnectPublicHostAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken
    )
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        IPAddress[] allowed = addresses.Where(AvatarDownloadPolicy.IsPublicAddress).ToArray();
        if (allowed.Length == 0)
            throw new InvalidDataException("Avatar host does not resolve to a public IP address.");

        Exception? lastError = null;
        foreach (IPAddress address in allowed)
        {
            Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e)
            {
                socket.Dispose();
                lastError = e;
            }
        }
        throw new IOException("Could not connect to an allowed avatar host.", lastError);
    }
}
