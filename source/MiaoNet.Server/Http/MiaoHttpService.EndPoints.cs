using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using MiaoNet.Shared;
namespace MiaoNet.Server;

public partial class MiaoHttpService
{
    // TODO uh, we may need to switch our connection id fully to auth id
    // TODO don't use MiaoServerServices directly, use sth like IMiaoServerService
    private async Task<List<(int AuthID, string Name)>> KickByAuthIDAsync(int aid, string reason)
    {
        List<(int, string)> kicked = new();
        foreach (var p in miaoServerService.ServerState.Players)
        {
            if (p.Value.Player.Info.AuthID == aid)
            {
                kicked.Add((aid, p.Value.Player.Info.Name));
                await p.Value.DisconnectAsync(DisconnectReason.Kicked, reason);
            }
        }
        return kicked;
    }

    private async Task<List<(int AuthID, string Name)>> KickByConnectionIDAsync(int cid, string reason)
    {
        if (!miaoServerService.Players.TryGetValue(cid, out var client))
            return new();
        await client.DisconnectAsync(DisconnectReason.Kicked, reason);
        return new() { (client.Player.Info.AuthID, client.Player.Info.Name) };
    }

    private Task BroadcastAnnouncementAsync(string message)
    {
        adminChatBuffer.Record("server", null, "服务器", 0, message);
        return miaoServerService.BroadcastAsync(new PacketChatMessage(DateTime.UtcNow, ChatMessageType.Server, null, message));
    }

    private async Task Player(NameValueCollection query, HttpListenerContext context)
    {
        switch (context.Request.HttpMethod)
        {
        case "DELETE":
        {
            string? reason = query["reason"];
            if (reason is null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                break;
            }
            if (int.TryParse(query["cid"], CultureInfo.InvariantCulture, out int cid))
            {
                var kicked = await KickByConnectionIDAsync(cid, reason);
                if (kicked.Count == 0)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    break;
                }
                // let everyone know the player was kicked
                await BroadcastAnnouncementAsync($"玩家 {kicked[0].Name} 已被踢出服务器，原因：{reason}");
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                break;
            }
            else if (int.TryParse(query["aid"], CultureInfo.InvariantCulture, out int aid))
            {
                // used by the forum suspend hook: the player got banned, say so publicly
                var kicked = await KickByAuthIDAsync(aid, reason);
                if (kicked.Count > 0)
                    await BroadcastAnnouncementAsync($"玩家 {string.Join('、', kicked.Select(static k => k.Name))} 因为 {reason} 被封禁");
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                break;
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                break;
            }
        }
        default:
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            break;
        }
        }
    }

    private async Task Status(NameValueCollection query, HttpListenerContext context)
    {
        context.Response.ContentType = MediaTypeNames.Application.Json;

#pragma warning disable IDE0037
        var response = new
        {
            PlayersCount = miaoServerService.Players.Count,
            Channels = miaoServerService.Channels.Select(static c => new
            {
                ID = c.Key,
                Name = c.Value.Info.Name,
                IsPrivate = c.Value.IsPrivate,
                Players = c.Value.Players.Select(static c => new
                {
                    ID = c.ID,
                    Name = c.Player.Info.Name,
                    Location = c.Player.Location.ToString()
                })
            })
        };
#pragma warning restore IDE0037

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        await JsonSerializer.SerializeAsync(context.Response.OutputStream, response, jsonSerializerOptions);
    }

    private async Task Announce(NameValueCollection query, HttpListenerContext context)
    {
        string? message = query["msg"];
        if (string.IsNullOrWhiteSpace(message))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }

        await BroadcastAnnouncementAsync(message);

        context.Response.StatusCode = (int)HttpStatusCode.NoContent;
    }

    private Task DoGC(NameValueCollection query, HttpListenerContext context)
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        context.Response.StatusCode = (int)HttpStatusCode.NoContent;
        return Task.CompletedTask;
    }

    private async Task GetMetrics(NameValueCollection query, HttpListenerContext context)
    {
        context.Response.ContentType = MediaTypeNames.Application.Json;

        var values = miaoMetricsService.Get();
        var ret = new
        {
            OnlinePlayersCount = miaoServerService.Players.Count,
            Metrics = values,
            GC = new
            {
                TotalAllocatedBytes = GC.GetTotalAllocatedBytes(),
                TotalMemory = GC.GetTotalMemory(false),
                TotalPauseDuration = GC.GetTotalPauseDuration()
            }
        };
        await JsonSerializer.SerializeAsync(context.Response.OutputStream, ret, jsonSerializerOptions);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
    }
}
