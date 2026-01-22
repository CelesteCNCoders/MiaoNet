#pragma warning disable IDE0060 // unused parameters

using System.Collections.Specialized;
using System.Net;
using System.Text;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging;
namespace MiaoNet.Server;

public partial class MiaoHttpService
{
    private async Task HandleRequestAsync(string path, NameValueCollection query, HttpListenerContext context)
    {
        try
        {
            switch (path)
            {
            case "/summary":
            case "/info":
                await Summary(query, context);
                break;
            case "/player/disconnect":
                await PlayerDisconnect(query, context);
                break;
            case "/announce":
                await Announce(query, context);
                break;
            case "/gc":
                await GC(query, context);
                break;
            default:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                break;
            }
        }
        catch (Exception e)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            logger.LogError(AppEvents.Http, e, "Error when handling request \"{url}\" from {ep}", context.Request.RawUrl, context.Request.RemoteEndPoint);
        }
    }
    private async Task PlayerDisconnect(NameValueCollection query, HttpListenerContext context)
    {
        if (context.Request.HttpMethod != HttpMethod.Post.Method)
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            return;
        }

        if (!int.TryParse(query["id"], out int pid))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }
        if (!miaoServerService.ServerState.AllPlayers.TryGetValue(pid, out var client))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }
        await client.Connection.DisconnectAsync(DisconnectReason.Kicked, "admin kicked.");
        context.Response.StatusCode = (int)HttpStatusCode.NoContent;
    }

    // TODO jsonify
    private Task Summary(NameValueCollection query, HttpListenerContext context)
    {
        StringBuilder sb = new(128);

        var state = miaoServerService.ServerState;

        sb.AppendLine($"Channels Count: {state.AllChannels.Count}");
        sb.AppendLine($"Players Count: {state.AllPlayers.Count}");
        sb.AppendLine();
        foreach ((_, var channel) in state.AllChannels)
        {
            sb.AppendLine($"Channel {channel.StateInfo}");
            foreach ((_, (var player, _)) in channel.Players)
            {
                sb.AppendLine($"  Player {player.Info} at {player.Location}, {player.State}");
            }
        }

        context.Response.OutputStream.Write(Encoding.UTF8.GetBytes(sb.ToString()));
        return Task.CompletedTask;
    }

    private async Task Announce(NameValueCollection query, HttpListenerContext context)
    {
        if (context.Request.HttpMethod != HttpMethod.Post.Method)
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            return;
        }

        string? message = query["msg"];
        if (string.IsNullOrWhiteSpace(message))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }

        foreach (var (_, (p, c)) in miaoServerService.ServerState.AllPlayers)
        {
            await c.QueuePacketAsync(new PacketChatMessage(DateTime.UtcNow, ChatMessageType.Server, null, message));
            context.Response.OutputStream.Write(Encoding.UTF8.GetBytes($"Announced to {p.Info}\n"));
        }
        context.Response.StatusCode = (int)HttpStatusCode.NoContent;
    }

    private Task GC(NameValueCollection query, HttpListenerContext context)
    {
        System.GC.Collect(System.GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        System.GC.WaitForPendingFinalizers();
        context.Response.OutputStream.Write(Encoding.UTF8.GetBytes("Done GC."));
        return Task.CompletedTask;
    }
}
