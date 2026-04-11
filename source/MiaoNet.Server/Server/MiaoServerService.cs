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
using System.Net.Security;
using System.Security.Authentication;

namespace MiaoNet.Server;

public sealed partial class MiaoServerService : BackgroundService
{
    private static readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;

    private readonly ILogger<MiaoServerService> logger;
    private readonly MiaoClientConnectionFactory connectionFactory;
    private readonly MiaoServerOptions options;
    private readonly IMiaoAuthenticator authenticator;
    private readonly MiaoMetricsService miaoMetricsService;

    private readonly PacketDispatcher packetDispatcher;

    private readonly INetworkListener networkListener;

    private readonly PeriodicTimer pingTimer;
    private readonly Stopwatch stopwatch;

    // all state data, for example, player infos, channel infos
    private readonly ServerState serverState;

    public ServerState ServerState => serverState;

    public int DisconnectTimeout => options.DisconnectTimeout;

    // TODO refactor
    public MiaoServerService(
        ILogger<MiaoServerService> logger,
        IOptions<MiaoServerOptions> options,
        NetworkListenerFactory networkListenerFactory,
        MiaoClientConnectionFactory connectionFactory,
        IMiaoAuthenticator authenticator,
        MiaoMetricsService miaoMetricsService
    )
    {
        serverState = new();

        PacketHandlerRegister register = new();
        RegisterPacketHandlers(register);
        packetDispatcher = new(register);

        this.logger = logger;
        this.connectionFactory = connectionFactory;
        this.authenticator = authenticator;
        this.options = options.Value;
        networkListener = networkListenerFactory(this.options.Network);
        pingTimer = new(TimeSpan.FromMilliseconds(this.options.PingPeriod));
        stopwatch = Stopwatch.StartNew();
        this.miaoMetricsService = miaoMetricsService;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("MiaoNet Server v{v} starting...", options.ExpectedVersion.ToString(3));
        logger.LogInformation("Start to listen on {ep}.", options.Network.ListenEndPoint);
        networkListener.Listen();
        _ = HandleConnectionsHeartbeats(cancellationToken);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            IPendingNetworkConnection pending = await networkListener.AcceptAsync(stoppingToken);
            var addr = pending.RemoteAddress;
            logger.LogInformation(AppEvents.Connection, "New client try connecting: {addr}", addr);
            _ = HandlePendingConnectionAsync(pending, stoppingToken);
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        pingTimer.Dispose();
    }

    private async Task HandlePendingConnectionAsync(IPendingNetworkConnection pendingConnection, CancellationToken token)
    {
        string addr = pendingConnection.RemoteAddress;
        INetworkConnection? networkConnection = null;
        HandshakeResult? handshakeResult;

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(options.HandshakeTimeout);
        var localToken = cts.Token;
        try
        {
            networkConnection = await pendingConnection.CompleteAsync(localToken);
            bool result;

            // we've finished TLS handshake
            // now check if it's a MiaoNet client
            result = await DoConnectionHeadCheckAsync(networkConnection, localToken);
            if (!result)
            {
                networkConnection.Dispose();
                return;
            }

            // now do version check
            result = await DoVersionCheckAsync(networkConnection, localToken);
            if (!result)
            {
                networkConnection.Shutdown();
                networkConnection.Dispose();
                return;
            }

            // version check finished, now make our own handshake
            handshakeResult = await DoHandshakeAsync(networkConnection, localToken);
            if (handshakeResult is null)
            {
                networkConnection.Shutdown();
                networkConnection.Dispose();
                return;
            }
        }
        catch (OperationCanceledException e)
        when (e.CancellationToken == cts.Token)
        {
            networkConnection?.Dispose();
            pendingConnection.Dispose();
            logger.LogInformation(AppEvents.Connection, "{addr} Handshake timeouted.", addr);
            return;
        }
        catch (Exception e)
        {
            networkConnection?.Dispose();
            pendingConnection.Dispose();
            logger.LogError(
                AppEvents.Connection, e,
                "Error when completing pending connection({addr}).",
                addr
            );
            return;
        }
        finally
        {
            cts.Dispose();
        }

        // now it's not "pending" for us
        await HandleConnectionAsync(networkConnection, handshakeResult);

        async Task<bool> DoConnectionHeadCheckAsync(INetworkConnection connection, CancellationToken token)
        {
            var stream = connection.Stream;

            var buffer = pool.Rent(Connection.HandshakeHeadLength);
            try
            {
                var memory = buffer.AsMemory(0, Connection.HandshakeHeadLength);
                await stream.ReadExactlyAsync(memory, token);
                bool equals = memory.Span.SequenceEqual(Connection.HandshakeHead.Span);
                if (!equals)
                {
                    logger.LogInformation(AppEvents.Connection, "{addr} is not a MiaoNet client.", connection.RemoteAddress);
                }
                return equals;
            }
            finally
            {
                pool.Return(buffer);
            }
        }

        // maybe we could improve our serialization implement...
        // this is ugly
        async Task<bool> DoVersionCheckAsync(INetworkConnection networkConnection, CancellationToken token)
        {
            var stream = networkConnection.Stream;

            var version = options.ExpectedVersion;
            ushort major = (ushort)version.Major, minor = (ushort)version.Minor, build = (ushort)version.Build;
            ushort majorClient, minorClient, buildClient;

            const int VersionLength = 3 * sizeof(ushort);
            byte[] buffer = pool.Rent(VersionLength);
            bool passed;
            try
            {
                var memory = buffer.AsMemory(0, VersionLength);

                await stream.ReadExactlyAsync(memory, token);
                var span = memory.Span;
                majorClient = BinaryPrimitives.ReadUInt16LittleEndian(span[0..2]);
                minorClient = BinaryPrimitives.ReadUInt16LittleEndian(span[2..4]);
                buildClient = BinaryPrimitives.ReadUInt16LittleEndian(span[4..6]);

                passed = major == majorClient && minor == minorClient && build == buildClient;
            }
            finally
            {
                pool.Return(buffer);
            }

            buffer = pool.Rent(1 + VersionLength);
            try
            {
                var memory = buffer.AsMemory(0, 1 + VersionLength);
                var span = memory.Span;
                if (!passed)
                {
                    span[0] = 0;
                    BinaryPrimitives.WriteUInt16LittleEndian(span[1..3], major);
                    BinaryPrimitives.WriteUInt16LittleEndian(span[3..5], minor);
                    BinaryPrimitives.WriteUInt16LittleEndian(span[5..7], build);
                    logger.LogInformation(
                        AppEvents.Connection,
                        "{addr} version {v1}.{v2}.{v3} does not match current version.",
                        networkConnection.RemoteAddress, majorClient, minorClient, buildClient
                    );
                    await stream.WriteAsync(memory[0..(1 + VersionLength)], token);
                    return false;
                }
                else
                {
                    span[0] = 1;
                    await stream.WriteAsync(memory[0..1], token);
                    return true;
                }
            }
            finally
            {
                pool.Return(buffer);
            }
        }

        async Task<HandshakeResult?> DoHandshakeAsync(INetworkConnection networkConnection, CancellationToken token)
        {
            var stream = networkConnection.Stream;
            ushort size;

            var buffer = pool.Rent(sizeof(ushort));
            try
            {
                var memory = buffer.AsMemory(0, sizeof(ushort));
                await stream.ReadExactlyAsync(memory, token);
                size = BinaryPrimitives.ReadUInt16LittleEndian(memory.Span);
            }
            finally
            {
                pool.Return(buffer);
            }

            HandshakeData handshakeData;
            buffer = pool.Rent(size);
            try
            {
                var memory = buffer.AsMemory(0, size);
                await stream.ReadExactlyAsync(memory, token);
                var span = memory.Span;
                RefBinaryReader reader = new(span);
                handshakeData = reader.Read<HandshakeData>();
            }
            finally
            {
                pool.Return(buffer);
            }

            var authResult = await authenticator.AuthenticateAsync(
                handshakeData.AuthenticationData,
                handshakeData.IsAuthorize,
                token
            );

            string? failedReason = authResult.IsFailed ? authResult.SuspendMessage : null;
            HandshakeAckData ack = new(authResult.Type, authResult.TokenData, failedReason);

            MemoryStream ms = new(32);
            ms.Seek(2, SeekOrigin.Begin);
            RefBinaryWriter writer = new(ms);
            writer.Write(ack);
            ushort ackSize = (ushort)(ms.Position - sizeof(ushort));
            ms.Seek(0, SeekOrigin.Begin);
            writer.Write(ackSize);
            Memory<byte> memoryToSend = ms.GetBuffer().AsMemory(0, ackSize + sizeof(ushort));
            await stream.WriteAsync(memoryToSend, token);

            return authResult.IsFailed ? null : new HandshakeResult(authResult.PlayerInfo, handshakeData, ack);
        }
    }

    private async Task HandleConnectionAsync(INetworkConnection connection, HandshakeResult handshakeResult)
    {
        miaoMetricsService.RecordSession();
        string addr = connection.RemoteAddress;
        try
        {
            // create the player
            var newPlayer = serverState.CreateNewPlayer(handshakeResult.PlayerInfo);
            logger.LogInformation(
                AppEvents.Connection,
                "Assign {ep}({player}) to id {id}.",
                addr,
                newPlayer.Info.Name,
                newPlayer.ID
            );

            // create the connection
            MiaoClientConnection newConnection = connectionFactory(newPlayer.ID, connection, newPlayer, this);

            // send the new player initial data
            PlayerInfo clientPlayerInfo = newConnection.Player.Info;

            ValueTask sendStateTask;
            Task tellOthersOneJoinedTask;
            ServerState.StateLock.EnterWriteLock();
            try
            {
                // fetch online players infos
                List<ChannelInfo> channels = serverState.AllChannels.Select(c => c.Value.StateInfo).ToList();
                var playerInfos =
                    from pair in serverState.AllPlayers
                    let p = pair.Value.Player
                    select new PacketClientInitial.Player(
                        p.Channel.ID, p.ID, p.Info, p.Location, p.GlobalFlags
                    );

                PacketClientInitial packetClientInitial = new PacketClientInitial(
                    newPlayer.Channel.ID,
                    newPlayer.ID,
                    clientPlayerInfo,
                    channels,
                    playerInfos.ToList()
                );

                // then send
                sendStateTask = newConnection.QueuePacketAsync(packetClientInitial);

                // other connections can see this player now
                serverState.AddPlayer(newPlayer, newConnection);

                // and then tell other clients a new player came
                tellOthersOneJoinedTask = BroadcastOthersAsync(
                    new PacketPlayerJoined(newPlayer.Channel.ID, newPlayer.ID, newPlayer.Info), newPlayer.ID
                );
            }
            finally
            {
                ServerState.StateLock.ExitWriteLock();
            }

            await sendStateTask;
            await tellOthersOneJoinedTask;

            // exchange data with this player
            await newConnection.HandleClientConnectAsync();

            // TODO don't do removing stuffs here

            // exchange finished, remove this player
            // this operation is lock-free
            serverState.RemovePlayer(newPlayer);

            // then, tell other clients this player left
            logger.LogInformation(AppEvents.Connection, "Client id {id} handle finished.", newPlayer.ID);
            await BroadcastAsync(new PacketPlayerLeft(newPlayer.ID));
        }
        catch (Exception e)
        {
            connection.Dispose();
            logger.LogError(AppEvents.Connection, e, "Exception occurred for {addr}:", addr);
        }
    }

    private async Task HandleConnectionsHeartbeats(CancellationToken token)
    {
    Restart:
        try
        {
            List<(Task<TimeSpan?>, MiaoClientConnection)> list = new();
            List<Task> taskList = new();
            while (await pingTimer.WaitForNextTickAsync(token))
            {
                foreach (var (_, (_, connection)) in ServerState.AllPlayers)
                    list.Add((PingFor(connection, options.HeartbeatTimeoutThreshold), connection));

                foreach (var item in list) taskList.Add(item.Item1);

                // TODO should we wait for all clients to response?
                await Task.WhenAll(taskList);
                PacketPingData pingData = new(
                    list.Where(t => t.Item1.Result is not null)
                        .Select(t => (t.Item2.ID, (int)t.Item1.Result!.Value.TotalMilliseconds)
                ).ToList());

                await BroadcastAsync(pingData);

                async Task<TimeSpan?> PingFor(MiaoClientConnection connection, int timeout)
                {
                    TaskCompletionSource responseTcs = new();
                    var start = stopwatch.Elapsed;
                    await connection.RequestAsync(new PacketPing(), OnResponse);

                    Task timeoutTask = Task.Delay(timeout, CancellationToken.None);
                    Task completedTask = await Task.WhenAny(responseTcs.Task, timeoutTask);
                    if (completedTask == responseTcs.Task)
                    {
                        var end = stopwatch.Elapsed;
                        return end - start;
                    }
                    else
                    {
                        logger.LogInformation(AppEvents.Connection, "{p} timeouted heartbeat.", connection.Player.Info);
                        await connection.DisconnectAsync(DisconnectReason.Timeout);
                        return null;
                    }

                    Task OnResponse(PacketPong pong)
                    {
                        responseTcs.SetResult();
                        return Task.CompletedTask;
                    }
                }

                list.Clear();
                taskList.Clear();
            }
        }
        catch (OperationCanceledException e)
        when (e.CancellationToken == token)
        {
            logger.LogInformation(AppEvents.Server, "Cancelled heartbeats task.");
        }
        catch (Exception e)
        {
            // wait what
            logger.LogCritical(AppEvents.Server, e, "Handler of connections heartbeats is down.");
            // we'd better not to make the server down too...
            goto Restart;
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
        => BroadcastContextuallyToAsync(
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
        return BroadcastContextuallyToAsync(
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

        return BroadcastContextuallyToAsync(
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
        SerializedPacket serializedPacket = new(packet, connectionsCount);
        List<Task>? bounded = null;
        int notMeetCount = 0;
        foreach (var connection in connections)
        {
            if (predicate(connection))
            {
                bool tryResult = connection.TryQueuePacket(serializedPacket);
                if (!tryResult)
                    (bounded ??= new()).Add(connection.QueuePacketAsync(serializedPacket).AsTask());
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

    private static Task BroadcastContextuallyToAsync(
        IContextualPacket packet,
        IEnumerable<MiaoClientConnection> connections,
        Predicate<MiaoClientConnection> predicate
    )
    {
        List<Task>? bounded = null;
        foreach (var connection in connections.Where(c => predicate(c)))
        {
            if (!connection.TryQueuePacket(packet))
            {
                (bounded ??= new()).Add(connection.QueuePacketAsync(packet).AsTask());
            }
        }

        if (bounded is not null)
            return Task.WhenAll(bounded);
        return Task.CompletedTask;
    }

    #endregion

    public async ValueTask HandlePacketAsync(MiaoClientConnection connection, IContextualPacket packet)
    {
        if (packet is PacketResponse res)
        {
            var handler = connection.OnResponse(res);
            if (handler is null)
            {
                logger.LogWarning(
                    AppEvents.Connection,
                    "Unknown received response of id {rid} for player {p}. Type is {type}",
                    res.RequestID,
                    connection.Player.Info,
                    packet.GetType()
                );
                return;
            }
            await handler(res);
        }
        else
        {
            bool handled = await packetDispatcher.DispatchPacketAsync(connection, packet);
            if (!handled)
            {
                logger.LogWarning("Unhandled packet received from client: {pc}", packet.GetType());
            }
        }
    }
}