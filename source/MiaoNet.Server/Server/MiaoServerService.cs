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
using System.Linq;

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

            var handshakeData = await handshakeTask;
            if (handshakeData is null)
            {
                cts.Cancel();
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
            MiaoClientConnection newConnection = new(newPlayer.ID, socket, newPlayer, conLogger, this);

            // send the new player initial data
            PlayerInfo clientPlayerInfo = newConnection.Player.Info;

            ValueTask sendStateTask;
            Task knowJoinedTask;
            ServerState.StateLock.EnterUpgradeableReadLock();
            try
            {
                List<ChannelInfo> channels = serverState.AllChannels.Select(c => c.Value.StateInfo).ToList();
                var playerInfos =
                    from pair in serverState.AllPlayers
                    let p = pair.Value.Player
                    select new PacketClientInitial.Player(
                        p.Channel.ID, p.Info, p.Location, p.OnlineStatus
                    );

                PacketClientInitial packetClientInitial = new PacketClientInitial(
                    newPlayer.Channel.ID,
                    clientPlayerInfo,
                    channels,
                    playerInfos.ToList()
                );
                //Thread.Sleep(Random.Shared.Next(1000, 5000));
                sendStateTask = newConnection.SendPacketAsync(packetClientInitial);

                ServerState.StateLock.EnterWriteLock();
                try
                {
                    // other connections can see this player now
                    serverState.AddPlayer(newPlayer, newConnection);

                    // and then tell other clients a new player came
                    knowJoinedTask = BroadcastOthersAsync(
                        new PacketPlayerJoined(newPlayer.Channel.ID, newPlayer.Info), newPlayer.ID
                    );
                }
                finally
                {
                    ServerState.StateLock.ExitWriteLock();
                }
            }
            finally
            {
                ServerState.StateLock.ExitUpgradeableReadLock();
            }

            await sendStateTask;
            await knowJoinedTask;

            // exchange data with this player
            await newConnection.HandleClientConnectAsync();

            // exchange finished, tell other clients this player left
            logger.LogInformation(AppEvents.Connection, "Client id {id} handle finished.", newPlayer.ID);
            await BroadcastAsync(new PacketPlayerLeft(newPlayer.ID));

            // then remove this player
            serverState.RemovePlayer(newPlayer);

            async Task<HandshakeData?> HandleHandshakeAsync(Socket socket, CancellationToken token)
            {
                EndPoint? ep = socket.RemoteEndPoint;
                NetworkStream netStream = new(socket);

                Task<HandshakeData?> receiveHandshake = ReceiveHandshakeAsync(netStream, token);
                Task completedTask = await Task.WhenAny(receiveHandshake, Task.Delay(options.HandshakeTimeout, token));

                if (completedTask != receiveHandshake)
                {
                    logger.LogDebug(AppEvents.Connection, "{ep} Handshake timeout.", ep);
                    return null;
                }
                if (receiveHandshake.Result is null)
                {
                    logger.LogDebug(AppEvents.Connection, "{ep} Handshake failed.", ep);
                    return null;
                }

                logger.LogDebug(AppEvents.Connection, "{ep} Handshake succeeded.", ep);

                var handshakeData = receiveHandshake.Result;

                Version requiredVersion = new Version(0, 1, 2);
                if (handshakeData.Version != requiredVersion)
                {
                    await SendHandshakeAck(netStream, $"Server requires version {requiredVersion}.", token);
                    logger.LogInformation(
                        AppEvents.Connection,
                        "{ep} version {v} not match current version.",
                        ep, handshakeData.Version
                    );
                    return null;
                }
                await SendHandshakeAck(netStream, null, token);

                return handshakeData;
            }

            async Task<HandshakeData?> ReceiveHandshakeAsync(Stream netStream, CancellationToken token)
            {
                const int TotalHeadSize = Connection.HandshakeHeadLength + sizeof(ushort);
                var buffer = new byte[TotalHeadSize];
                await netStream.ReadExactlyAsync(buffer, token);

                if (!buffer.AsSpan()[..^sizeof(ushort)].SequenceEqual(Connection.HandshakeHead.Span))
                    return null;
                ushort size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan()[^sizeof(ushort)..]);

                buffer = new byte[size];
                await netStream.ReadExactlyAsync(buffer, token);
                RefBinaryReader reader = new(buffer);
                return reader.Read<HandshakeData>();
            }

            async Task SendHandshakeAck(Stream netStream, string? deniedReason, CancellationToken token)
            {
                MemoryStream ms = new(32);
                ms.Seek(sizeof(ushort), SeekOrigin.Begin);
                RefBinaryWriter writer = new(ms);
                writer.Write(new HandshakeAckData(deniedReason));
                ushort length = (ushort)(ms.Position - sizeof(ushort));
                ms.Seek(0, SeekOrigin.Begin);
                writer.Write(length);

                await netStream.WriteAsync(ms.GetBuffer().AsMemory(0, length + sizeof(ushort)), token);
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
    public Task BroadcastAsync(IContextlessPacket packet)
        => BroadcastToAsync(packet, _ => true);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastOthersAsync(IContextlessPacket packet, int selfID)
        => BroadcastToAsync(packet, c => c.ID != selfID);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastContextuallyOthersAsync(IContextualPacket packet, int selfID)
        => BoradcastContextuallyToAsync(
            packet,
            ServerState.AllPlayers.Select(p => p.Value.Connection),
            c => c.ID != selfID
        );

    /// <inheritdoc cref="BroadcastToAsync(IPacket, IEnumerable{MiaoClientConnection}, Predicate{MiaoClientConnection}, int)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastToAsync(IContextlessPacket packet, Predicate<MiaoClientConnection> predicate)
    {
        var players = serverState.AllPlayers;
        return BroadcastToAsync(packet, players.Select(p => p.Value.Connection), predicate, players.Count);
    }

    /// <inheritdoc cref="BroadcastToAsync(IPacket, IEnumerable{MiaoClientConnection}, Predicate{MiaoClientConnection}, int)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastToOthersAsync(IContextlessPacket packet, Predicate<MiaoClientConnection> predicate, int selfID)
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
    public Task BroadcastToAsync(IContextlessPacket packet, ServerChannel channel, Predicate<MiaoClientConnection> predicate)
    {
        Debug.Assert(serverState.AllChannels.ContainsValue(channel));

        var players = channel.Players;
        return BroadcastToAsync(packet, players.Select(p => p.Value.Connection), predicate, players.Count);
    }

    /// <inheritdoc cref="BroadcastToAsync(IPacket, IEnumerable{MiaoClientConnection}, Predicate{MiaoClientConnection}, int)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastToOthersAsync(
        IContextlessPacket packet,
        ServerChannel channel,
        Predicate<MiaoClientConnection> predicate,
        int selfID
    )
    {
        Debug.Assert(serverState.AllChannels.ContainsValue(channel));

        var players = channel.Players;
        return BroadcastToAsync(
            packet,
            players.Select(p => p.Value.Connection),
            c => c.ID != selfID && predicate(c),
            players.Count
        );
    }

    public Task BroadcastContextuallyToOthersAsync(
        IContextualPacket packet,
        Predicate<MiaoClientConnection> predicate,
        int selfID
    )
    {
        return BoradcastContextuallyToAsync(
            packet,
            ServerState.AllPlayers.Select(p => p.Value.Connection),
            c => c.ID != selfID && predicate(c)
        );
    }

    public Task BroadcastContextuallyToOthersAsync(
        IContextualPacket packet,
        ServerChannel channel,
        Predicate<MiaoClientConnection> predicate,
        int selfID
    )
    {
        Debug.Assert(serverState.AllChannels.ContainsValue(channel));

        return BoradcastContextuallyToAsync(
            packet,
            channel.Players.Select(p => p.Value.Connection),
            c => c.ID != selfID && predicate(c)
        );
    }

    /// <summary>
    /// Broadcast a packet to all clients that meet <paramref name="predicate"/>.
    /// All predicate will be tested before the first <see langword="await"/>.
    /// </summary>
    private static Task BroadcastToAsync(
        IContextlessPacket packet,
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

    private static Task BoradcastContextuallyToAsync(
        IContextualPacket packet,
        IEnumerable<MiaoClientConnection> connections,
        Predicate<MiaoClientConnection> predicate
    )
    {
        List<Task>? bounded = null;
        foreach (var connection in connections.Where(c => predicate(c)))
        {
            var serializedPacket = new SerializedPacket(ArrayPool<byte>.Shared, packet, connection);
            if (!connection.TrySendPacket(serializedPacket))
                (bounded ??= new()).Add(connection.SendPacketAsync(serializedPacket).AsTask());
        }

        if (bounded is not null)
            return Task.WhenAll(bounded);
        return Task.CompletedTask;
    }

    #endregion

    public async ValueTask HandlePacketAsync(MiaoClientConnection connection, IContextualPacket packet)
    {
        if (!await packetDispatcher.DispatchPacketAsync(connection, packet))
            logger.LogWarning("Unhandled packet received from client: {pc}", packet.GetType());
    }
}