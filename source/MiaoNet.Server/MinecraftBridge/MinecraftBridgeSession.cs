using System.Net.WebSockets;
using System.Text.Json;
using MiaoNet.Shared;

namespace MiaoNet.Server;

/// <summary>
/// One connected Minecraft (Paper) bridge endpoint.
/// Owns the socket send side and the set of virtual players it reported.
/// </summary>
public sealed class MinecraftBridgeSession
{
    private static readonly JsonSerializerOptions serializerOptions = new();

    private readonly WebSocket socket;
    private readonly SemaphoreSlim sendLock = new(1, 1);

    public int ID { get; }

    public string Remote { get; }

    public string? ServerName { get; set; }

    /// <summary>Guards <see cref="Players"/>.</summary>
    public object SyncRoot { get; } = new();

    /// <summary>Minecraft player name -> virtual player.</summary>
    public Dictionary<string, ServerPlayer> Players { get; } = new(StringComparer.Ordinal);

    public MinecraftBridgeSession(int id, WebSocket socket, string remote)
    {
        ID = id;
        this.socket = socket;
        Remote = remote;
    }

    public async Task SendAsync<T>(T message, CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, serializerOptions);
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        finally
        {
            sendLock.Release();
        }
    }

    public ValueTask<ValueWebSocketReceiveResult> SocketReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        => socket.ReceiveAsync(buffer, cancellationToken);

    /// <summary>Fire-and-forget send for event handlers; failures are swallowed
    /// (a broken socket will be cleaned up by the receive loop).</summary>
    public void TrySend<T>(T message)
        => _ = TrySendCoreAsync(message);

    private async Task TrySendCoreAsync<T>(T message)
    {
        try
        {
            await SendAsync(message, CancellationToken.None);
        }
        catch
        {
        }
    }
}
