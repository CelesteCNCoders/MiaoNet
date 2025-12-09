using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using MiaoNet.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiaoNet.Server;

public sealed partial class MiaoServerService : BackgroundService
{
    private readonly ILogger<MiaoServerService> logger;
    private readonly ILoggerFactory connectionLoggerFactory;
    private readonly MiaoServerOptions options;

    private readonly PacketDispatcher packetDispatcher;

    private readonly IPEndPoint listenIPEndPoint;
    private readonly Socket acceptSocket;

    // all state data, for example, player infos, channel infos
    private readonly ServerState serverState;

    public ServerState ServerState => serverState;

    public MiaoServerService(
        ILogger<MiaoServerService> logger,
        IOptions<MiaoServerOptions> options,
        ILoggerFactory connectionLoggerFactory
        )
    {
        serverState = new();

        PacketHandlerRegister register = new();
        RegisterPacketHandlers(register);
        packetDispatcher = new(register);

        this.logger = logger;
        this.connectionLoggerFactory = connectionLoggerFactory;
        this.options = options.Value;

        listenIPEndPoint = IPEndPoint.Parse(this.options.ListenIPEndPoint);
        acceptSocket = new(SocketType.Stream, ProtocolType.Tcp);
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Start to listen on port {ep}.", listenIPEndPoint);
        acceptSocket.Bind(listenIPEndPoint);
        acceptSocket.Listen();
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            Socket socket = await acceptSocket.AcceptAsync(stoppingToken);
            socket.NoDelay = true;
            var ep = socket.RemoteEndPoint;
            logger.LogInformation(AppEvents.Connection, "New client try connecting: {ep}", ep);
            _ = HandleConnectionAsync(socket, stoppingToken);
        }
    }

    private async Task HandleConnectionAsync(Socket socket, CancellationToken token)
    {
        EndPoint? ep = socket.RemoteEndPoint;
        try
        {
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            // make handshake first
            Task<HandshakeData?> handshakeTask = HandleHandshakeAsync(socket, cts.Token);
            Task completedTask = await Task.WhenAny(handshakeTask, Task.Delay(options.HandshakeTimeout, cts.Token));
            if (completedTask != handshakeTask)
            {
                cts.Cancel();
                logger.LogDebug(AppEvents.Connection, "{ep} Handshake timeout.", ep);
                return;
            }
            if (handshakeTask.Result is null)
            {
                cts.Cancel();
                logger.LogDebug(AppEvents.Connection, "{ep} Handshake failed.", ep);
                return;
            }

            logger.LogDebug(AppEvents.Connection, "{ep} Handshake succeeded.", ep);

            var handshakeData = handshakeTask.Result;

            if (handshakeData.Version != new Version(0, 1, 0))
            {
                // TODO tell the client what's wrong with them
                cts.Cancel();
                logger.LogInformation(
                    AppEvents.Connection,
                    "{ep} version {v} not match current version.",
                    ep, handshakeData.Version
                );
                return;
            }

            // then create the connection and the player
            var newPlayer = serverState.CreateNewPlayer(handshakeData);
            logger.LogInformation(
                AppEvents.Connection,
                "Assign {ep}({player}) to id {id}.",
                ep,
                newPlayer.Info.Name,
                newPlayer.ID
            );

            var conLogger = connectionLoggerFactory.CreateLogger<MiaoClientConnection>();
            MiaoClientConnection connection = new(newPlayer.ID, socket, newPlayer, conLogger, this);

            // send the new player initial data
            PlayerInfo clientPlayerInfo = connection.Player.Info;
            List<ChannelStateInfo> channels = serverState.AllChannels.Select(c => c.Value.StateInfo).ToList();
            var playerInfos =
                from pair in serverState.AllPlayers
                let p = pair.Value.Player
                select new PacketPlayerJoined(
                    p.GetChannelPlayerLocationInfo(),
                    null,
                    newPlayer.ShouldSyncFrom(p) ? p.State : null
                );

            PacketClientInitial packetClientInitial = new PacketClientInitial(clientPlayerInfo, channels, playerInfos.ToList());
            await connection.SendPacketAsync(packetClientInitial);

            // other connections can see this player now
            serverState.AddPlayer(newPlayer, connection);

            // and then tell other clients a new player came
            serverState.StateLock.EnterReadLock();
            Task withStateTask, generalTask;
            try
            {
                // this part seems similiar...
                // can we make it... hm... a method?
                IPacket withStatePacket = new PacketPlayerJoined(
                    newPlayer.GetChannelPlayerLocationInfo(),
                    newPlayer.GraphicsInfo,
                    newPlayer.State
                );
                IPacket generalPacket = new PacketPlayerJoined(newPlayer.GetChannelPlayerLocationInfo());
                withStateTask = BroadcastToOthersAsync(
                    withStatePacket,
                    con => con.Player.ShouldSyncFrom(newPlayer),
                    connection.ID
                );
                generalTask = BroadcastToOthersAsync(
                    generalPacket,
                    con => !con.Player.ShouldSyncFrom(newPlayer),
                    connection.ID
                );
            }
            finally
            {
                serverState.StateLock.ExitReadLock();
            }
            await withStateTask;
            await generalTask;

            // exchange data with this player
            await connection.HandleClientConnectAsync();

            // exchange finished, tell other clients this player left
            logger.LogInformation(AppEvents.Connection, "Client id {id} handle finished.", newPlayer.ID);
            await BroadcastAsync(new PacketPlayerLeft(newPlayer.ID));

            // then remove this player
            serverState.RemovePlayer(newPlayer);

            async Task<HandshakeData?> HandleHandshakeAsync(Socket socket, CancellationToken token)
            {
                try
                {
                    const int TotalHeadSize = Connection.HandshakeHeadLength + sizeof(ushort);
                    var buffer = new byte[TotalHeadSize];
                    int totalReceived = 0;
                    while (totalReceived < TotalHeadSize)
                    {
                        int received = await socket.ReceiveAsync(buffer.AsMemory().Slice(totalReceived), token);
                        if (received == 0)
                            return null;
                        totalReceived += received;
                    }
                    if (!buffer.AsSpan()[..^2].SequenceEqual(Connection.HandshakeHead))
                        return null;
                    ushort size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan()[^2..]);

                    // TODO size
                    buffer = new byte[size];
                    totalReceived = 0;
                    while (totalReceived < size)
                    {
                        int received = await socket.ReceiveAsync(buffer.AsMemory().Slice(totalReceived), token);
                        if (received == 0)
                            return null;
                        totalReceived += received;
                    }
                    RefBinaryReader reader = new(buffer);
                    return reader.Read<HandshakeData>();
                }
                catch (Exception e)
                {
                    logger.LogWarning(AppEvents.Connection, e, "Exception when receiving handshake data.");
                    return null;
                }
            }
        }
        catch (Exception e)
        {
            logger.LogError(AppEvents.Connection, e, "Exception occurred for {ep}:", ep);
        }
        finally
        {
            if (socket.Connected)
                socket.Shutdown(SocketShutdown.Both);
            socket.Close();
        }
    }

    #region tons of broadcasting

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastAsync(IPacket packet)
        => BroadcastToAsync(packet, _ => true);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastOthersAsync(IPacket packet, int selfID)
        => BroadcastToAsync(packet, c => c.ID != selfID);

    /// <inheritdoc cref="BroadcastToAsync(IPacket, IEnumerable{MiaoClientConnection}, Predicate{MiaoClientConnection}, int)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastToAsync(IPacket packet, Predicate<MiaoClientConnection> predicate)
    {
        var players = serverState.AllPlayers;
        return BroadcastToAsync(packet, players.Select(p => p.Value.Connection), predicate, players.Count);
    }

    /// <inheritdoc cref="BroadcastToAsync(IPacket, IEnumerable{MiaoClientConnection}, Predicate{MiaoClientConnection}, int)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastToOthersAsync(IPacket packet, Predicate<MiaoClientConnection> predicate, int selfID)
    {
        var players = serverState.AllPlayers;
        return BroadcastToAsync(
            packet,
            players.Select(p => p.Value.Connection),
            c => c.ID != selfID && predicate(c),
            players.Count
        );
    }

    /// <inheritdoc cref="BroadcastToAsync(IPacket, IEnumerable{MiaoClientConnection}, Predicate{MiaoClientConnection}, int)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastToAsync(IPacket packet, ServerChannel channel, Predicate<MiaoClientConnection> predicate)
    {
        Debug.Assert(serverState.AllChannels.ContainsValue(channel));

        var players = channel.Players;
        return BroadcastToAsync(packet, players.Select(p => p.Value.Connection), predicate, players.Count);
    }

    /// <inheritdoc cref="BroadcastToAsync(IPacket, IEnumerable{MiaoClientConnection}, Predicate{MiaoClientConnection}, int)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastToOthersAsync(
        IPacket packet,
        ServerChannel channel,
        Predicate<MiaoClientConnection> predicate,
        int selfID
    )
    {
        Debug.Assert(serverState.AllChannels.ContainsValue(channel));

        var players = channel.Players;
        return BroadcastToAsync(packet, players.Select(p => p.Value.Connection), c => c.ID != selfID && predicate(c), players.Count);
    }

    /// <summary>
    /// Broadcast a packet to all clients that meet <paramref name="predicate"/>.
    /// All predicate will be tested before the first <see langword="await"/>.
    /// </summary>
    private static Task BroadcastToAsync(
        IPacket packet,
        IEnumerable<MiaoClientConnection> connections,
        Predicate<MiaoClientConnection> predicate,
        int connectionsCount
    )
    {
        SerializedPacket serializedPacket = new(ArrayPool<byte>.Shared, packet, connectionsCount);
        List<Task>? bounded = null;
        int notMeetCount = 0;
        foreach (var connection in connections)
        {
            if (predicate(connection))
            {
                if (!connection.TrySendPacket(serializedPacket))
                    (bounded ??= new()).Add(connection.SendPacketAsync(serializedPacket).AsTask());
            }
            else
            {
                notMeetCount++;
            }
        }
        serializedPacket.OnConsumed(notMeetCount);
        if (bounded is not null)
            return Task.WhenAll(bounded);
        return Task.CompletedTask;
    }

    #endregion

    public async ValueTask HandlePacketAsync(MiaoClientConnection connection, IPacket packet)
    {
        if (!await packetDispatcher.DispatchPacketAsync(connection, packet))
            logger.LogWarning("Unhandled packet received from client: {pc}", packet.GetType());
    }
}