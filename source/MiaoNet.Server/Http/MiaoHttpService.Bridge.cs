using System.Collections.Specialized;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MiaoNet.Server;

public partial class MiaoHttpService
{
    private async Task HandleMinecraftBridgeRequestAsync(NameValueCollection query, HttpListenerContext context)
    {
        if (!minecraftBridgeService.Enabled)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(
                context.Response.OutputStream,
                new { error = "This endpoint expects a WebSocket upgrade request." },
                jsonSerializerOptions
            );
            return;
        }
        if (!CheckBridgeToken(query, context))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(
                context.Response.OutputStream,
                new { error = "Missing or invalid X-Api-Token header (or token query parameter)." },
                jsonSerializerOptions
            );
            return;
        }

        var webSocketContext = await context.AcceptWebSocketAsync(null);
        // the session owns the socket now; keep this handler alive until it ends
        await minecraftBridgeService.HandleSessionAsync(
            webSocketContext.WebSocket,
            context.Request.RemoteEndPoint,
            serviceStopping.Token
        );
    }

    private bool CheckBridgeToken(NameValueCollection query, HttpListenerContext context)
    {
        // the bridge is a write endpoint: unlike /status it must never be open,
        // so an unconfigured ApiToken means no bridge access at all
        if (string.IsNullOrEmpty(apiToken))
            return false;

        string? token = context.Request.Headers["X-Api-Token"] ?? query["token"];
        if (token is null)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(apiToken)
        );
    }
}
