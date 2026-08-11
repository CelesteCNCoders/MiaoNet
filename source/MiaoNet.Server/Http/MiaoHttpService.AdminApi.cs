using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

public sealed partial class MiaoHttpService
{
    private sealed record AdminKickRequest(int? AuthID, int? ConnectionID, string? Reason);

    private sealed record AdminAnnounceRequest(string? Message);

    private async Task HandleAdminApiRequestAsync(
        string path,
        NameValueCollection query,
        HttpListenerContext context,
        AdminSessionStore.AdminSession session
    )
    {
        try
        {
            switch (path)
            {
            case "/admin/api/logs":
                await AdminApiLogsAsync(query, context);
                break;
            case "/admin/api/chat":
                await AdminApiChatAsync(query, context);
                break;
            case "/admin/api/players":
                await AdminApiPlayersAsync(context);
                break;
            case "/admin/api/kick":
                await AdminApiKickAsync(context, session);
                break;
            case "/admin/api/announce":
                await AdminApiAnnounceAsync(context, session);
                break;
            case "/admin/api/metrics":
                await AdminApiMetricsAsync(context);
                break;
            default:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                break;
            }
        }
        catch (Exception e)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            logger.LogError(AppEvents.Http, e, "Error when handling admin api request \"{url}\".", context.Request.RawUrl);
        }
    }

    private async Task AdminApiLogsAsync(NameValueCollection query, HttpListenerContext context)
    {
        if (context.Request.HttpMethod != "GET")
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            return;
        }
        long after = ParseLong(query["after"], -1);
        int limit = ClampLimit(ParseLong(query["limit"], 200), 500);

        var entries = adminLogBuffer.GetAfter(after, limit);
        await WriteJsonAsync(context, (int)HttpStatusCode.OK, new
        {
            entries = entries.Select(static e => new
            {
                id = e.Id,
                time = e.Time,
                level = e.Level.ToString(),
                category = e.Category,
                message = e.Message,
                exception = e.Exception
            }),
            latest = adminLogBuffer.LatestId
        });
    }

    private async Task AdminApiChatAsync(NameValueCollection query, HttpListenerContext context)
    {
        if (context.Request.HttpMethod != "GET")
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            return;
        }
        long after = ParseLong(query["after"], -1);
        int limit = ClampLimit(ParseLong(query["limit"], 100), 500);

        var entries = adminChatBuffer.GetAfter(after, limit);
        await WriteJsonAsync(context, (int)HttpStatusCode.OK, new
        {
            entries = entries.Select(static e => new
            {
                id = e.Id,
                time = e.Time,
                type = e.Type,
                channel = e.ChannelName,
                player = e.PlayerName,
                authID = e.AuthID,
                content = e.Content
            }),
            latest = adminChatBuffer.LatestId
        });
    }

    private async Task AdminApiPlayersAsync(HttpListenerContext context)
    {
        if (context.Request.HttpMethod != "GET")
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            return;
        }
        var players = miaoServerService.Players.Select(static p => new
        {
            connectionID = p.Key,
            name = p.Value.Player.Info.Name,
            authID = p.Value.Player.Info.AuthID,
            channel = p.Value.Player.Channel.StateInfo.Name,
            location = p.Value.Player.Location.ToString()
        });
        var channels = miaoServerService.Channels.Select(static c => new
        {
            id = c.Key,
            name = c.Value.StateInfo.Name,
            players = c.Value.Players.Count
        });
        await WriteJsonAsync(context, (int)HttpStatusCode.OK, new { players, channels });
    }

    private async Task AdminApiKickAsync(HttpListenerContext context, AdminSessionStore.AdminSession session)
    {
        if (context.Request.HttpMethod != "POST")
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            return;
        }
        AdminKickRequest? request = await ReadJsonBodyAsync<AdminKickRequest>(context);
        if (request is null || (request.AuthID is null && request.ConnectionID is null))
        {
            await WriteJsonAsync(context, (int)HttpStatusCode.BadRequest,
                new { ok = false, error = "需要提供 authID 或 connectionID" });
            return;
        }
        string reason = string.IsNullOrWhiteSpace(request.Reason) ? "你已被管理员踢出" : request.Reason!;

        int kicked = 0;
        if (request.ConnectionID is int cid)
            kicked += await KickByConnectionIDAsync(cid, reason);
        if (request.AuthID is int aid)
            kicked += await KickByAuthIDAsync(aid, reason);

        logger.LogInformation(
            AppEvents.Http,
            "Admin {admin} kicked {count} player(s) (authID: {aid}, connectionID: {cid}), reason: {reason}.",
            session.UserName, kicked, request.AuthID, request.ConnectionID, reason
        );
        await WriteJsonAsync(context, (int)HttpStatusCode.OK,
            kicked > 0
                ? (object)new { ok = true, kicked }
                : new { ok = false, error = "未找到该玩家" });
    }

    private async Task AdminApiAnnounceAsync(HttpListenerContext context, AdminSessionStore.AdminSession session)
    {
        if (context.Request.HttpMethod != "POST")
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            return;
        }
        AdminAnnounceRequest? request = await ReadJsonBodyAsync<AdminAnnounceRequest>(context);
        if (request is null || string.IsNullOrWhiteSpace(request.Message))
        {
            await WriteJsonAsync(context, (int)HttpStatusCode.BadRequest,
                new { ok = false, error = "公告内容不能为空" });
            return;
        }
        await BroadcastAnnouncementAsync(request.Message);
        logger.LogInformation(AppEvents.Http, "Admin {admin} broadcasted announcement: {msg}.", session.UserName, request.Message);
        await WriteJsonAsync(context, (int)HttpStatusCode.OK, new { ok = true });
    }

    private async Task AdminApiMetricsAsync(HttpListenerContext context)
    {
        if (context.Request.HttpMethod != "GET")
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            return;
        }
        var (current, series) = adminMetricsSampler.GetSnapshot();
        var metrics = miaoMetricsService.Get();
        await WriteJsonAsync(context, (int)HttpStatusCode.OK, new
        {
            current = new
            {
                time = current.Time,
                onlinePlayers = current.OnlinePlayers,
                channels = current.Channels,
                sessions = metrics.SessionsCount,
                chatMessagesTotal = adminChatBuffer.TotalCount,
                upPacketsPerSecond = current.UpPacketsPerSecond,
                downPacketsPerSecond = current.DownPacketsPerSecond,
                upBytesPerSecond = current.UpBytesPerSecond,
                downBytesPerSecond = current.DownBytesPerSecond,
                uptimeSeconds = adminMetricsSampler.UptimeSeconds,
                gcTotalMemory = GC.GetTotalMemory(false)
            },
            series = new
            {
                time = series.Select(static s => s.Time),
                onlinePlayers = series.Select(static s => s.OnlinePlayers),
                channels = series.Select(static s => s.Channels),
                upPacketsPerSecond = series.Select(static s => s.UpPacketsPerSecond),
                downPacketsPerSecond = series.Select(static s => s.DownPacketsPerSecond),
                upBytesPerSecond = series.Select(static s => s.UpBytesPerSecond),
                downBytesPerSecond = series.Select(static s => s.DownBytesPerSecond),
                chatMessagesPerInterval = series.Select(static s => s.ChatMessagesPerInterval)
            }
        });
    }

    private async Task<T?> ReadJsonBodyAsync<T>(HttpListenerContext context)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(
                context.Request.InputStream,
                jsonSerializerOptions
            );
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private async Task WriteJsonAsync(HttpListenerContext context, int statusCode, object value)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.OutputStream, value, jsonSerializerOptions);
    }

    private static long ParseLong(string? value, long fallback)
        => long.TryParse(value, CultureInfo.InvariantCulture, out long result) ? result : fallback;

    private static int ClampLimit(long limit, int max)
        => (int)Math.Clamp(limit, 1, max);
}
