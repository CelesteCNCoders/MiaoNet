using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiaoNet.Server;

/// <summary>
/// Bridges Minecraft (Paper) servers into MiaoNet over /ws/minecraft.
/// Minecraft players appear as virtual players (negative IDs) in a persistent
/// channel; chat is relayed in both directions. Neither side generates
/// join/leave chat notifications for the other side - only the player
/// lists are synchronized.
/// </summary>
public sealed class MinecraftBridgeService : IDisposable
{
    public const int ProtocolVersion = 1;

    // Minecraft chat allows up to 256 chars; MiaoNet clients cap their input at 64,
    // but server-injected messages (like announcements) may be longer
    private const int MaxChatContentLength = 256;

    private const int MaxMessageBytes = 16 * 1024;

    // a Minecraft-grass green for virtual player names
    private static readonly Color VirtualPlayerColor = new(85, 255, 85);

    private static readonly JsonSerializerOptions deserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<MinecraftBridgeService> logger;
    private readonly MiaoServerService miaoServerService;
    private readonly AdminChatBuffer adminChatBuffer;
    private readonly MinecraftBridgeOptions options;
    private readonly ConcurrentDictionary<int, MinecraftBridgeSession> sessions = new();
    private int nextSessionID;

    private readonly ServerChannel? minecraftChannel;

    /// <summary>Only valid when <see cref="Enabled"/>; sessions cannot exist otherwise.</summary>
    private ServerChannel Channel => minecraftChannel!;

    public bool Enabled => options.Enabled;

    /// <summary>Null when the bridge is disabled (no channel is created then).</summary>
    public ServerChannel? MinecraftChannel => minecraftChannel;

    public MinecraftBridgeService(
        ILogger<MinecraftBridgeService> logger,
        IOptions<MinecraftBridgeOptions> options,
        MiaoServerService miaoServerService,
        AdminChatBuffer adminChatBuffer
    )
    {
        this.logger = logger;
        this.miaoServerService = miaoServerService;
        this.adminChatBuffer = adminChatBuffer;
        this.options = options.Value;

        if (this.options.Enabled)
        {
            minecraftChannel = GetOrCreateChannel(this.options.ChannelName);
            miaoServerService.ChatMessageEmitted += OnChatMessageEmitted;
            miaoServerService.PlayerJoinedOnline += OnPlayerListChanged;
            miaoServerService.PlayerLeftOnline += OnPlayerListChanged;
        }
    }

    public void Dispose()
    {
        miaoServerService.ChatMessageEmitted -= OnChatMessageEmitted;
        miaoServerService.PlayerJoinedOnline -= OnPlayerListChanged;
        miaoServerService.PlayerLeftOnline -= OnPlayerListChanged;
    }

    private ServerChannel GetOrCreateChannel(string name)
    {
        ServerState state = miaoServerService.ServerState;
        if (state.TryGetChannelByName(name, out var existing))
        {
            existing.IsPersistent = true;
            existing.AllowClientJoin = false;
            return existing;
        }
        ServerChannel channel = state.CreateNewChannel(new ChannelInfo(name));
        channel.IsPersistent = true;
        // virtual players only: real Celeste clients may not join this channel
        channel.AllowClientJoin = false;
        state.AddChannel(channel);
        if (channel.IsPrivate)
        {
            logger.LogWarning(
                AppEvents.Server,
                "Minecraft bridge channel name {name} starts with '!' and is therefore private; "
                + "virtual players in it will be hidden from other channels.",
                name
            );
        }
        logger.LogInformation(AppEvents.Server, "Created persistent Minecraft bridge channel {name} (id {id}).", name, channel.ID);
        return channel;
    }

    #region session lifecycle

    public async Task HandleSessionAsync(WebSocket socket, EndPoint? remote, CancellationToken cancellationToken)
    {
        int id = Interlocked.Increment(ref nextSessionID);
        MinecraftBridgeSession session = new(id, socket, remote?.ToString() ?? "?");
        sessions[id] = session;
        logger.LogInformation(AppEvents.Http, "Minecraft bridge session {id} from {ep} connected.", id, session.Remote);
        try
        {
            await session.SendAsync(
                new
                {
                    type = "hello_ack",
                    serverVersion = Connection.Version.ToString(3),
                    protocol = ProtocolVersion,
                    channelName = Channel.Info.Name
                },
                cancellationToken
            );
            await session.SendAsync(BuildOnlineMessage(), cancellationToken);
            await ReceiveLoopAsync(session, cancellationToken);
        }
        catch (WebSocketException e)
        {
            logger.LogInformation(AppEvents.Http, "Minecraft bridge session {id} socket error: {msg}", id, e.Message);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            sessions.TryRemove(id, out _);
            await RemoveAllSessionPlayersAsync(session);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch
                {
                }
            }
            socket.Dispose();
            logger.LogInformation(AppEvents.Http, "Minecraft bridge session {id} disconnected.", id);
        }
    }

    private async Task ReceiveLoopAsync(MinecraftBridgeSession session, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxMessageBytes);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using MemoryStream ms = new();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await session.SocketReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    ms.Write(buffer, 0, result.Count);
                    if (ms.Length > MaxMessageBytes)
                    {
                        await session.SendAsync(new { type = "error", message = "Message too large." }, cancellationToken);
                        return;
                    }
                } while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                string json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                await HandleMessageAsync(session, json, cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task HandleMessageAsync(MinecraftBridgeSession session, string json, CancellationToken cancellationToken)
    {
        string? type;
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(json);
            type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        }
        catch (JsonException)
        {
            type = null;
        }

        try
        {
            switch (type)
            {
            case "hello":
            {
                var hello = doc!.RootElement.Deserialize<BridgeHelloMessage>(deserializeOptions);
                session.ServerName = hello?.ServerName;
                logger.LogInformation(
                    AppEvents.Http,
                    "Minecraft bridge session {id} says hello from server {name} (protocol {p}).",
                    session.ID, session.ServerName, hello?.Protocol
                );
                break;
            }
            case "players":
            {
                var msg = doc!.RootElement.Deserialize<BridgePlayersMessage>(deserializeOptions);
                if (msg is not null)
                    await ApplyPlayersSnapshotAsync(session, msg.Players);
                break;
            }
            case "chat":
            {
                var msg = doc!.RootElement.Deserialize<BridgeChatInMessage>(deserializeOptions);
                if (msg is not null)
                    await ApplyChatAsync(session, msg.Player, msg.Content);
                break;
            }
            default:
                await session.SendAsync(new { type = "error", message = "Unknown or missing message type." }, cancellationToken);
                break;
            }
        }
        catch (JsonException e)
        {
            logger.LogWarning(AppEvents.Http, "Malformed bridge message from session {id}: {msg}", session.ID, e.Message);
            await session.SendAsync(new { type = "error", message = "Malformed message." }, cancellationToken);
        }
        finally
        {
            doc?.Dispose();
        }
    }

    #endregion

    #region Minecraft -> MiaoNet

    // the wire PlayerLocation has no coordinate field, so the room string carries
    // "dimension (x, y, z)"; clients render it in the online list as-is
    private static string EncodeRoom(string dimension, double x, double y, double z)
        => dimension.Length == 0
            ? string.Empty
            : $"{dimension} ({(int)Math.Round(x)}, {(int)Math.Round(y)}, {(int)Math.Round(z)})";

    private static PlayerLocation RoomToLocation(string room)
        => room.Length == 0
            ? PlayerLocation.Empty
            : new PlayerLocation("Minecraft", AreaMode.Normal, room);

    private ServerPlayer CreateVirtualPlayer(string name, string room)
    {
        PlayerInfo info = new(0, name, "Minecraft", string.Empty, VirtualPlayerColor);
        ServerPlayer player = miaoServerService.ServerState.CreateNewVirtualPlayer(info, Channel);
        player.Location = RoomToLocation(room);
        return player;
    }

    private async Task ApplyPlayersSnapshotAsync(MinecraftBridgeSession session, List<BridgePlayerEntry> players)
    {
        Dictionary<string, string> incoming = new(StringComparer.Ordinal);
        foreach (var p in players)
        {
            if (!string.IsNullOrWhiteSpace(p.Name))
                incoming[p.Name.Trim()] = EncodeRoom(p.Dimension?.Trim() ?? string.Empty, p.X, p.Y, p.Z);
        }

        List<ServerPlayer> removed = new();
        List<ServerPlayer> added = new();
        List<ServerPlayer> moved = new();
        bool announceChannel = false;
        lock (session.SyncRoot)
        {
            // clients auto-remove locally-emptied channels; if the bridge channel has no
            // virtual players right now, long-connected clients may have dropped it and
            // must re-learn it before any PacketPlayerJoined references it
            bool channelWasEmpty = !miaoServerService.ServerState.VirtualPlayers.Values.Any(p => p.Channel == Channel);

            MinecraftPlayerListDiffer.Diff(
                session.Players.ToDictionary(p => p.Key, p => p.Value.Location.Room, StringComparer.Ordinal),
                incoming,
                out var addedNames, out var removedNames, out var movedNames
            );

            foreach (var name in removedNames)
            {
                var player = session.Players[name];
                session.Players.Remove(name);
                if (miaoServerService.ServerState.RemoveVirtualPlayer(player.ID, out var p))
                    removed.Add(p);
            }
            foreach (var name in addedNames)
            {
                ServerPlayer player = CreateVirtualPlayer(name, incoming[name]);
                session.Players[name] = player;
                miaoServerService.ServerState.AddVirtualPlayer(player);
                added.Add(player);
            }
            foreach (var name in movedNames)
            {
                ServerPlayer player = session.Players[name];
                player.Location = RoomToLocation(incoming[name]);
                moved.Add(player);
            }
            announceChannel = added.Count > 0 && channelWasEmpty;
        }

        // silent list updates only: no join/leave chat notifications are generated
        if (announceChannel)
        {
            await MiaoServerService.BroadcastToScopeAsync(
                new PacketChannelCreated(Channel.ID, Channel.Info),
                miaoServerService.ServerState
            );
        }
        foreach (var player in removed)
            await MiaoServerService.BroadcastToScopeAsync(new PacketPlayerLeft(player.ID), miaoServerService.ServerState);
        foreach (var player in added)
        {
            await MiaoServerService.BroadcastToScopeAsync(
                new PacketPlayerJoined(Channel.ID, player.ID, player.Info),
                miaoServerService.ServerState
            );
        }
        foreach (var player in moved)
        {
            await MiaoServerService.BroadcastToScopeAsync(
                new PacketPlayerLocationChangedNotification(player.ID, player.Location, null),
                miaoServerService.ServerState
            );
        }
    }

    private async Task ApplyChatAsync(MinecraftBridgeSession session, string playerName, string content)
    {
        content = content.Trim();
        if (content.Length == 0)
            return;
        if (content.Length > MaxChatContentLength)
            content = content[..MaxChatContentLength];

        ServerPlayer player;
        bool joined = false;
        bool channelWasEmpty = false;
        lock (session.SyncRoot)
        {
            // a chat may arrive before any players snapshot; auto-add the sender
            if (!session.Players.TryGetValue(playerName, out player!))
            {
                channelWasEmpty = !miaoServerService.ServerState.VirtualPlayers.Values.Any(p => p.Channel == Channel);
                player = CreateVirtualPlayer(playerName, string.Empty);
                session.Players[playerName] = player;
                miaoServerService.ServerState.AddVirtualPlayer(player);
                joined = true;
            }
        }
        if (joined)
        {
            // re-announce the channel first if clients may have dropped it (see ApplyPlayersSnapshotAsync)
            if (channelWasEmpty)
            {
                await MiaoServerService.BroadcastToScopeAsync(
                    new PacketChannelCreated(Channel.ID, Channel.Info),
                    miaoServerService.ServerState
                );
            }
            await MiaoServerService.BroadcastToScopeAsync(
                new PacketPlayerJoined(Channel.ID, player.ID, player.Info),
                miaoServerService.ServerState
            );
        }

        bool global = options.RelayScope is MinecraftRelayScope.Global;
        ChatMessageType type = global ? ChatMessageType.Chat : ChatMessageType.ChannelChat;
        adminChatBuffer.Record(
            global ? "global" : "channel",
            global ? null : Channel.Info.Name,
            player.Info.Name, player.Info.AuthID, content
        );
        await MiaoServerService.BroadcastToScopeAsync(
            new PacketChatMessage(DateTime.UtcNow, type, player.ID, content),
            global ? miaoServerService.ServerState : Channel
        );
        // let other bridge sessions (and future sinks) see it; the owning session is excluded
        miaoServerService.RaiseChatMessageEmitted(player, type, content);
    }

    private async Task RemoveAllSessionPlayersAsync(MinecraftBridgeSession session)
    {
        List<ServerPlayer> removed = new();
        lock (session.SyncRoot)
        {
            foreach (var (_, player) in session.Players)
            {
                if (miaoServerService.ServerState.RemoveVirtualPlayer(player.ID, out var p))
                    removed.Add(p);
            }
            session.Players.Clear();
        }
        foreach (var player in removed)
            await MiaoServerService.BroadcastToScopeAsync(new PacketPlayerLeft(player.ID), miaoServerService.ServerState);
    }

    #endregion

    #region MiaoNet -> Minecraft

    private static string ColorToHex(Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private object BuildOnlineMessage()
    {
        // real players only: Minecraft servers don't need their own virtual players echoed back
        var players = miaoServerService.ServerState.Players.Values
            .Select(c => new
            {
                id = c.ID,
                name = c.Player.Info.Name,
                prefix = c.Player.Info.Prefix,
                color = ColorToHex(c.Player.Info.Color),
                location = c.Player.Location.ToString(),
                channel = c.Player.Channel.Info.Name
            })
            .ToArray();
        return new { type = "online", players };
    }

    private void OnPlayerListChanged(ServerPlayer player)
    {
        if (sessions.IsEmpty)
            return;
        var msg = BuildOnlineMessage();
        foreach (var (_, session) in sessions)
            session.TrySend(msg);
    }

    private void OnChatMessageEmitted(ServerPlayer? source, ChatMessageType type, string content)
    {
        if (sessions.IsEmpty)
            return;

        string scope;
        int? exceptSessionID = null;
        if (source is not null && source.ID < 0)
        {
            // a Minecraft player's own message: relay to other bridge sessions,
            // but never echo it back to the session that owns this player
            exceptSessionID = FindOwnerSessionID(source.ID);
            scope = "minecraft";
        }
        else
        {
            switch (type)
            {
            case ChatMessageType.Chat:
                scope = "global";
                break;
            case ChatMessageType.Server:
            case ChatMessageType.ServerChat:
                scope = "server";
                break;
            case ChatMessageType.ChannelChat when source?.Channel == Channel:
                scope = "channel";
                break;
            default:
                // map chat, private messages and other channels' chat stay inside MiaoNet
                return;
            }
        }

        var msg = new
        {
            type = "chat",
            player = source?.Info.Name ?? "Server",
            prefix = source?.Info.Prefix ?? string.Empty,
            // the MiaoNet client renders "[prefix] name" entirely in this color;
            // null for server announcements (rendered as announcements there)
            color = source is null ? null : ColorToHex(source.Info.Color),
            content,
            scope,
            time = DateTime.UtcNow
        };
        foreach (var (id, session) in sessions)
        {
            if (id != exceptSessionID)
                session.TrySend(msg);
        }
    }

    private int? FindOwnerSessionID(int virtualPlayerID)
    {
        foreach (var (id, session) in sessions)
        {
            lock (session.SyncRoot)
            {
                foreach (var (_, player) in session.Players)
                {
                    if (player.ID == virtualPlayerID)
                        return id;
                }
            }
        }
        return null;
    }

    #endregion
}
