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

            var player = serverState.CreateNewPlayer(handshakeTask.Result);
            logger.LogInformation(AppEvents.Connection, "Assign {ep}({player}) to id {id}.", ep, player.Info.Name, player.ID);

            var conLogger = connectionLoggerFactory.CreateLogger<MiaoClientConnection>();
            MiaoClientConnection connection = new(player.ID, socket, player, conLogger, this);

            PlayerInfo clientPlayerInfo = connection.Player.Info;
            List<ChannelStateInfo> channels = serverState.AllChannels.Select(c => c.Value.LocationInfo).ToList();
            List<ChannelPlayerLocationInfo> playerInfos = serverState.AllPlayers
                .Select(p => p.Value.Player)
                .Select(p => new ChannelPlayerLocationInfo(p.Channel.ID, p.Info, p.LocationInfo))
                .ToList();

            PacketClientInitial packetClientInitial = new PacketClientInitial(clientPlayerInfo, channels, playerInfos);
            await connection.SendPacketAsync(new SerializedPacket(ArrayPool<byte>.Shared, packetClientInitial, 1));

            serverState.AddPlayer(player, connection);

            await BroadcastOthersAsync(new PacketPlayerJoined(connection.Player.GetChannelPlayerLocationInfo()), connection);
            await connection.HandleClientConnectAsync();

            logger.LogInformation(AppEvents.Connection, "Client id {id} handle finished.", player.ID);

            serverState.RemovePlayer(player);

            await BroadcastAsync(new PacketPlayerLeft(player.ID));

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastAsync(IPacket packet)
        => BroadcastToAsync(packet, _ => true);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastOthersAsync(IPacket packet, MiaoClientConnection self)
        => BroadcastToAsync(packet, c => c != self);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastOthersAsync(IPacket packet, int selfID)
        => BroadcastToAsync(packet, c => c.ID != selfID);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastToAsync(IPacket packet, Predicate<MiaoClientConnection> predicate)
    {
        var players = serverState.AllPlayers;
        return BroadcastToAsync(packet, players.Select(p => p.Value.Connection), predicate, players.Count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastToOthersAsync(IPacket packet, Predicate<MiaoClientConnection> predicate, MiaoClientConnection self)
    {
        var players = serverState.AllPlayers;
        return BroadcastToAsync(packet, players.Select(p => p.Value.Connection), c => c != self && predicate(c), players.Count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastToOthersAsync(IPacket packet, Predicate<MiaoClientConnection> predicate, int selfID)
    {
        var players = serverState.AllPlayers;
        return BroadcastToAsync(packet, players.Select(p => p.Value.Connection), c => c.ID != selfID && predicate(c), players.Count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastToAsync(IPacket packet, ServerChannel channel, Predicate<MiaoClientConnection> predicate)
    {
        Debug.Assert(serverState.AllChannels.ContainsValue(channel));

        var players = channel.Players;
        return BroadcastToAsync(packet, players.Select(p => p.Value.Connection), predicate, players.Count);
    }

    private static Task BroadcastToAsync(
        IPacket packet,
        IEnumerable<MiaoClientConnection> connections,
        Predicate<MiaoClientConnection> predicate,
        int connectionsCount
    )
    {
        SerializedPacket serializedPacket = new(ArrayPool<byte>.Shared, packet, connectionsCount);
        List<Task>? bounded = null;
        foreach (var connection in connections)
        {
            if (predicate(connection))
            {
                if (!connection.TrySendPacket(serializedPacket))
                    (bounded ??= new()).Add(connection.SendPacketAsync(serializedPacket).AsTask());
            }
            else
            {
                serializedPacket.OnConsumed();
            }
        }
        if (bounded is not null)
            return Task.WhenAll(bounded);
        return Task.CompletedTask;
    }

    public async ValueTask HandlePacketAsync(MiaoClientConnection connection, IPacket packet)
    {
        if (!await packetDispatcher.DispatchPacketAsync(connection, packet))
            logger.LogWarning("Unhandled packet received from client: {pc}", packet.GetType());
    }
}