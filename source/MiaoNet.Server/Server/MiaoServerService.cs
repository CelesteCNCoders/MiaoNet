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

public sealed partial class MiaoServerService : BackgroundService, IMiaoServerService
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

    private readonly ReaderWriterLockSlim stateLock;
    private readonly ServerState serverState;

    public ServerState ServerState => serverState;

    public int DisconnectTimeout => options.DisconnectTimeout;

    public MiaoServerService(
        ILogger<MiaoServerService> logger,
        IOptions<MiaoServerOptions> options,
        NetworkListenerFactory networkListenerFactory,
        MiaoClientConnectionFactory connectionFactory,
        IMiaoAuthenticator authenticator,
        MiaoMetricsService miaoMetricsService
    )
    {
        stateLock = new();
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
        logger.LogInformation("MiaoNet Server v{v} starting...", Connection.Version.ToString(3));
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
            MiaoClientConnection newConnection = connectionFactory(connection, newPlayer, this);

            // send the new player initial data
            PlayerInfo clientPlayerInfo = newConnection.Player.Info;

            ValueTask sendStateTask;
            Task tellOthersOneJoinedTask;

            // we need consistent players+channels list and also to modify it
            // so acquire a write lock here
            using (stateLock.AcquireWriteLock())
            {
                // fetch online players infos
                var channels = serverState.Channels
                    .Select(c => new PacketClientInitial.Channel(c.Value.ID, c.Value.Info));
                var playerInfos =
                    from pair in serverState.Players
                    let p = pair.Value.Player
                    select new PacketClientInitial.Player(
                        p.Channel.ID, p.ID, p.Info, p.Location, p.GlobalFlags
                    );

                var strings = options.Announcements[handshakeResult.HandshakeData.LanguageCode];
                PacketClientInitial packetClientInitial = new PacketClientInitial(
                    newPlayer.Channel.ID,
                    newPlayer.ID,
                    clientPlayerInfo,
                    channels.ToList(),
                    playerInfos.ToList(),
                    new(strings.PlayerJoined, strings.PlayerLeft),
                    strings.PlayerJoinMessage
                );

                // then send
                sendStateTask = newConnection.QueuePacketAsync(packetClientInitial);

                // other connections can see this player now
                serverState.AddPlayer(newConnection);

                // and then tell other clients a new player came
                tellOthersOneJoinedTask = BroadcastOthersAsync(
                    new PacketPlayerJoined(newPlayer.Channel.ID, newPlayer.ID, newPlayer.Info), newPlayer.ID
                );
            }

            await sendStateTask;
            await tellOthersOneJoinedTask;

            // exchange data with this player
            await newConnection.HandleClientConnectAsync();

            // TODO don't do removing stuffs here
            using (stateLock.AcquireWriteLock())
            {
                serverState.RemovePlayer(newConnection);
            }
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
                foreach (var (_, connection) in serverState.Players)
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

    // TODO avoid closure in predicate

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
            serverState.Players.Select(p => p.Value),
            c => c.ID != selfID
        );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastToAsync(IContextlessPacket packet, Predicate<MiaoClientConnection> predicate)
        => BroadcastToAsync(packet, serverState.Players.Select(p => p.Value), predicate);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task BroadcastToOthersAsync(IContextlessPacket packet, Predicate<MiaoClientConnection> predicate, int selfID)
        => BroadcastToAsync(
            packet,
            serverState.Players.Select(p => p.Value),
            c => c.ID != selfID && predicate(c)
        );

    public Task BroadcastContextuallyToOthersAsync(
        IContextualPacket packet,
        Predicate<MiaoClientConnection> predicate,
        int selfID
    ) => BroadcastContextuallyToAsync(
            packet,
            serverState.Players.Select(p => p.Value),
            c => c.ID != selfID && predicate(c)
        );

    public Task BroadcastContextuallyToAsync(
        IContextualPacket packet,
        Predicate<MiaoClientConnection> predicate
    ) => BroadcastContextuallyToAsync(
            packet,
            serverState.Players.Select(p => p.Value),
            predicate
        );

    private static Task BroadcastToAsync(
        IContextlessPacket packet,
        IEnumerable<MiaoClientConnection> connections,
        Predicate<MiaoClientConnection> predicate
    ) => BroadcastContextuallyToAsync(packet, connections, predicate);

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