using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging;
namespace MiaoNet.Server;

public partial class MiaoHttpService
{
    private async Task PlayerKick(NameValueCollection query, HttpListenerContext context)
    {
        string? reason = query["reason"];
        if (!int.TryParse(query["id"], CultureInfo.InvariantCulture, out int pid))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }
        if (!miaoServerService.ServerState.AllPlayers.TryGetValue(pid, out var client))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }
        await client.Connection.DisconnectAsync(DisconnectReason.Kicked, reason ?? "admin kicked.");
        context.Response.StatusCode = (int)HttpStatusCode.NoContent;
    }

    private async Task Status(NameValueCollection query, HttpListenerContext context)
    {
        context.Response.ContentType = "application/json";

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

        await JsonSerializer.SerializeAsync(context.Response.OutputStream, response, jsonSerializerOptions);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
    }

    private async Task Announce(NameValueCollection query, HttpListenerContext context)
    {
        string? message = query["msg"];
        if (string.IsNullOrWhiteSpace(message))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }

        var players = miaoServerService.ServerState.AllPlayers;
        foreach (var (_, (p, c)) in players)
            await c.QueuePacketAsync(new PacketChatMessage(DateTime.UtcNow, ChatMessageType.Server, null, message));

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
        context.Response.ContentType = "application/json";

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
