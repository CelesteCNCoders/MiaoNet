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
    private async Task<int> KickByAuthIDAsync(int aid, string reason)
    {
        int kicked = 0;
        foreach (var p in miaoServerService.ServerState.AllPlayers)
        {
            if (p.Value.Player.Info.AuthID == aid)
            {
                await p.Value.Connection.DisconnectAsync(DisconnectReason.Kicked, reason);
                kicked++;
            }
        }
        return kicked;
    }

    private Task BroadcastAnnouncementAsync(string message)
        => miaoServerService.BroadcastAsync(new PacketChatMessage(DateTime.UtcNow, ChatMessageType.Server, null, message));

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
                if (!miaoServerService.ServerState.AllPlayers.TryGetValue(cid, out var client))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    break;
                }
                await client.Connection.DisconnectAsync(DisconnectReason.Kicked, reason);
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                break;
            }
            else if (int.TryParse(query["aid"], CultureInfo.InvariantCulture, out int aid))
            {
                await KickByAuthIDAsync(aid, reason);
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

        var state = miaoServerService.ServerState;

#pragma warning disable IDE0037
        var response = new
        {
            PlayersCount = state.AllPlayers.Count,
            Channels = state.AllChannels.Select(static c => new
            {
                ID = c.Key,
                Name = c.Value.StateInfo.Name,
                Players = c.Value.Players.Select(static p => new
                {
                    ID = p.Key,
                    Name = p.Value.Player.Info.Name,
                    Location = p.Value.Player.Location.ToString()
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
            OnlinePlayersCount = miaoServerService.ServerState.AllPlayers.Count,
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
