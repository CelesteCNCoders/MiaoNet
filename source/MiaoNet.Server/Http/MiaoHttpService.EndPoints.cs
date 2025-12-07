#pragma warning disable IDE0060 // unused parameters

using System.Collections.Specialized;
using System.Net;
using System.Text;
namespace MiaoNet.Server;

public partial class MiaoHttpService
{
    private void DispatchQuest(string path, NameValueCollection query, HttpListenerContext context)
    {
        switch (path)
        {
        case "/summary":
        case "/info":
            Summary(query, context);
            break;
        case "/player/disconnect":
            PlayerDisconnect(query, context);
            break;
        default:
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            break;
        }
    }

    private void PlayerDisconnect(NameValueCollection query, HttpListenerContext context)
    {
        if (!int.TryParse(query["id"], out int pid))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }
        if(!miaoServerService.ServerState.AllPlayers.TryGetValue(pid,out var client))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }
        client.Connection.Disconnect(Shared.KickedReason.Manually);
        context.Response.StatusCode = (int)HttpStatusCode.NoContent;
    }

    // TODO jsonify
    private void Summary(NameValueCollection query, HttpListenerContext context)
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
    }
}
