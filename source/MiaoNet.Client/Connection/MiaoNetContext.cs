using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using MiaoNet.ClientShared;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MiaoNetContext : IPacketSerializationContext
{
    private const int MaxPacketsPerUpdate = 64;
    private static readonly long ReceiveQueueBudgetTicks = Stopwatch.Frequency / 1000;

    private int currentRequestID;
    // request id -> on response handler
    private readonly ConcurrentDictionary<int, Action<PacketResponse>> pendingRequests;
    //private int warningTimes;

    private readonly ConnectionLifecycleCoordinator connectionLifecycle;
    private ConnectionOperation? activeConnectionOperation;
    private readonly ConcurrentPacketPriorityQueue<ReceivedPacket> receiveQueue;
    private long currentReceivedPacketTimestamp;

    private readonly record struct ReceivedPacket(
        long Generation,
        IContextualPacket Packet,
        long EnqueuedAt
    );

    private readonly ConcurrentQueue<Action> mainThreadQueue;

    private readonly List<MiaoNetComponent> components;
    private readonly List<MiaoNetComponent> renderableComponents;
    private MiaoServerConnection? connection;
    private readonly PacketDispatcher packetDispatcher;

    private ClientState? clientState;

    private bool hasComponentFocus;

    /// <summary>Update on Connect() call.</summary>
    public bool ShowAvatar { get; private set; }

#if DEBUG
    public string TargetServer { get; set; } = "127.0.0.1";
#else
    public string TargetServer { get; set; } = "s.saplonily.top";
#endif

    public int TargetPort { get; set; } = 21473;

    public bool HasComponentFocus
    {
        get => hasComponentFocus;
        set { SafeGuard.Assert(hasComponentFocus != value); hasComponentFocus = value; }
    }

    public bool IsSuitableToOpenUI
    {
        get
        {
            var scene = Engine.Scene;
#pragma warning disable IDE0260
            return scene.Entities.Any(e => e is KeyboardConfigUI or ButtonConfigUI) == false &&
                   // we can't check TextInputEXT.IsTextInputActive since ImGuiHelper is always activating it
                   ((scene as Overworld)?.Current is not OuiFileNaming and not UI.OuiModOptionString) &&
                   !scene.Entities.OfType<TextMenu>().Any(m => m.Items.Any(i => i is TextMenuExt.Modal { Visible: true })) &&
                   // do not open ui when it's teleporting using CollabLobbyUI
                   // but why level.Overlay is null at this time??
                   scene is not LevelLoader &&
                   !HasComponentFocus &&
                   (scene as Level)?.Overlay == null;
#pragma warning restore IDE0260
        }
    }

    // TODO avoid allowing null values
    public PooledStringManager? PooledStringManager { get; private set; }

    [NotNull]
    public PlayerPresenceMessage? PlayerPresenceMessage
    {
        get { EnsureState(); return field!; }
        private set;
    }

    PooledStringManager IPacketSerializationContext.PooledStringManager
    {
        get { EnsureState(); return PooledStringManager!; }
    }

    [MemberNotNullWhen(true, nameof(connection), nameof(ClientState), nameof(PlayerPresenceMessage))]
    public bool HasConnection => connection is not null && clientState is not null;

    public ClientState? ClientState => clientState;

    public ServerFeatureFlags ServerFeatures { get; private set; }

    public MainComponent MainComponent { get; }

    internal long CurrentReceivedPacketTimestamp
        => currentReceivedPacketTimestamp != 0
            ? currentReceivedPacketTimestamp
            : Stopwatch.GetTimestamp();

    public EmoteComponent EmoteComponent { get; }

    public ChatComponent ChatComponent { get; }

    public StatusComponent StatusComponent { get; }

    public MiaoNetContext()
    {
        RuntimeHelpers.RunClassConstructor(typeof(MiaoNetFont).TypeHandle);

        receiveQueue = new();
        pendingRequests = new();
        mainThreadQueue = new();
        connectionLifecycle = new();

        var main = MainComponent = new MainComponent(this);
        var pl = new PlayerListComponent(this);
        var chat = ChatComponent = new ChatComponent(this);
        var dm = new DebugMapComponent(this);
        var em = EmoteComponent = new EmoteComponent(this);
        components = [main, pl, chat, dm, em];
        renderableComponents = [dm, chat, pl];

        StatusComponent = new(this);
        PacketHandlerRegister r = new();
        RegisterPacketHandlers(r);
        packetDispatcher = new(r);
    }

    public void QueueConnect()
        => mainThreadQueue.Enqueue(new Action(Connect));

    public void Connect()
    {
        if (activeConnectionOperation is not null)
            return;

        // TODO hmmm tbh this is ugly, we'd better get a more elegant way to do this
        ShowAvatar = MiaoNetModule.Settings.ShowAvatar;
        long generation = connectionLifecycle.Begin();
#if USE_CELEMIAO_AUTH
        string? authenticationCode = ClientRC.AuthenticationCode;
        ClientRC.AuthenticationCode = null;
        ConnectionOperation operation = new(
            generation,
            ShowAvatar,
            TargetServer,
            TargetPort,
            authenticationCode,
            MiaoNetModule.Settings.TokenData
        );
#else
        ConnectionOperation operation = new(generation, ShowAvatar, TargetServer, TargetPort);
#endif
        activeConnectionOperation = operation;

        Thread connectionThread = new(new ParameterizedThreadStart(ConnectionThread));
        connectionThread.Name = $"MiaoNet Connection {generation}";
        connectionThread.IsBackground = true;
        connectionThread.Start(operation);
        StatusComponent.ShowStatusMessage(ConnectionStatus.Connecting, true);
    }

    public void OnConnected()
    {
        components.ForEach(c => c.OnConnected());
    }

    public void Disconnect()
    {
        if (activeConnectionOperation is not null)
        {
            StatusComponent.ShowStatusMessage(
                connection is not null ? ConnectionStatus.Disconnected : ConnectionStatus.Cancelled
            );
        }
        OnDisconnected(activeConnectionOperation?.Generation);
    }

    public void DisconnectByException(Exception exception)
    {
        StatusComponent.ShowStatusMessage(ConnectionStatus.DisconnectedWithLocalReason(exception.Message));
        OnDisconnected(activeConnectionOperation?.Generation);
    }

    public void OnDisconnected()
        => OnDisconnected(activeConnectionOperation?.Generation);

    private void OnDisconnected(long? generation)
    {
        if (generation is null)
            return;

        ConnectionOperation? operation = activeConnectionOperation;
        if (operation is null || operation.Generation != generation.Value)
            return;
        if (!connectionLifecycle.TryEnd(generation.Value))
            return;

        // Stop publishing the connection before invoking extensible cleanup code.
        // A cleanup callback can fail or re-enter this method, but observers must
        // never see a live connection paired with an already-cleared client state.
        bool hadConnection = connection is not null;
        activeConnectionOperation = null;
        connection = null;
        clientState = null;
        PlayerPresenceMessage = null;
        ServerFeatures = ServerFeatureFlags.None;
        hasComponentFocus = false;
        PooledStringManager = null;

        List<PacketDisconnected>? terminalPackets = null;
        List<CleanupStep> cleanupSteps =
        [
            new("cancel connection operation", operation.Cancel),
            new("drain receive queue", () =>
            {
                while (TryDequeueReceivedPacket(
                    out long receivedGeneration,
                    out IContextualPacket receivedPacket,
                    out _
                ))
                {
                    if (receivedGeneration == generation.Value && receivedPacket is PacketDisconnected dc)
                        (terminalPackets ??= []).Add(dc);
                }
            }),
            new("clear pending requests", pendingRequests.Clear),
        ];
        if (components is not null)
        {
            cleanupSteps.AddRange(components.Select<MiaoNetComponent, CleanupStep>(component =>
                new($"disconnect component {component.GetType().FullName}", component.OnDisconnected)));
        }

        List<CleanupStep> finalSteps =
        [
            new("close connection operation", () => operation.CloseConnection(false)),
        ];
        if (hadConnection)
            finalSteps.Add(new("persist avatar state", AvatarManager.PersistStateToDisk));

        List<CleanupFailure> failures = [.. BestEffortCleanup.Run(cleanupSteps, finalSteps)];

        if (terminalPackets is not null)
        {
            foreach (PacketDisconnected packet in terminalPackets)
            {
                failures.AddRange(BestEffortCleanup.Run(
                    [new("dispatch terminal disconnect packet", () => packetDispatcher.DispatchPacket(packet))],
                    []));
            }
        }

        foreach (CleanupFailure failure in failures)
        {
            Logger.Error(LT.MiaoNet, $"Failed to {failure.StepName}.");
            Logger.LogDetailed(failure.Exception, LT.MiaoNet);
        }
    }

    public void Update()
    {
        try
        {
            while (mainThreadQueue.TryDequeue(out var item))
                item();

            StatusComponent.Update();

            if (!HasConnection)
                return;

            while (mainThreadQueue.TryDequeue(out var item))
                item();

            int packetsHandled = 0;
            long receiveQueueStartedAt = Stopwatch.GetTimestamp();
            while (packetsHandled < MaxPacketsPerUpdate
                && TryDequeueReceivedPacket(
                    out long receivedGeneration,
                    out IContextualPacket receivedPacket,
                    out long enqueuedAt
                ))
            {
                if (connectionLifecycle.IsCurrent(receivedGeneration))
                {
                    currentReceivedPacketTimestamp = enqueuedAt;
                    try
                    {
                        HandleQueuedPacket(receivedPacket);
                    }
                    finally
                    {
                        currentReceivedPacketTimestamp = 0;
                    }
                }
                packetsHandled++;
                if (Stopwatch.GetTimestamp() - receiveQueueStartedAt >= ReceiveQueueBudgetTicks)
                    break;
            }

            if (!HasConnection)
                return;

            components.ForEach(c => c.Update());
        }
        catch (Exception e)
        {
            Logger.Error(LT.MiaoNet, "Exception occurred during updating!");
            Logger.LogDetailed(e, LT.MiaoNet);
            DisconnectByException(e);
        }
    }

    private void OnInitialized(ConnectionOperation operation, MiaoServerConnection connection, PacketClientInitial packetClientInitial)
    {
        if (!connectionLifecycle.TryMarkConnected(operation.Generation))
            return;

#if USE_CELEMIAO_AUTH
        MiaoNetModule.Settings.LastName = packetClientInitial.SelfPlayerInfo.Name;
        if (operation.RefreshedAuthenticationData is not null)
        {
            MiaoNetModule.Settings.TokenData = operation.RefreshedAuthenticationData;
            Logger.Info(LT.MiaoNetConnection, "Server sent new auth data, accepted.");
        }
#endif
        ServerFeatures = packetClientInitial.ServerFeatures;
        clientState = new(packetClientInitial);
        PlayerPresenceMessage = packetClientInitial.PlayerPresenceMessage;
        PooledStringManager = operation.PooledStringManager;
        this.connection = connection;
        ClientInitialized?.Invoke(clientState);
        StatusComponent.ShowStatusMessage(ConnectionStatus.Connected);
        foreach (var line in packetClientInitial.JoinMessage.EnumerateLines())
            ChatComponent.AddLocalChat(MiaoNetChatText.CreateAnnouncement(line.ToString()));
        OnConnected();
    }

    public bool CanUseWatchSceneSync(OnlinePlayer target)
        => WatchProtocolCompatibility.SupportsWatchSceneSync(ServerFeatures, target.GlobalFlags);

    // warn: this is called on Connection Thread
    private bool HandleDirectPacket(ConnectionOperation operation, MiaoServerConnection connection, IContextualPacket packet)
    {
        if (!connectionLifecycle.IsCurrent(operation.Generation))
            return true;

        if (packet is PacketPing ping)
        {
            PacketPong pong = new() { RequestID = ping.RequestID };
            connection.QueuePacket(pong);
            return true;
        }
        else if (packet is PacketPlayerJoined joined)
        {
            if (operation.ShowAvatar)
            {
                SynchronizationContext.Current!.Post(async s =>
                {
                    PacketPlayerJoined joined = (PacketPlayerJoined)s!;
                    await SafePrepareAvatarAsync(operation, joined.PlayerID, joined.PlayerInfo);
                }, joined);
            }
        }
        return false;
    }

    private async Task SafePrepareAvatarAsync(ConnectionOperation operation, int playerID, PlayerInfo playerInfo)
    {
        SafeGuard.Assert(operation.ShowAvatar);
        try
        {
            string sid = $"\0mn_avt_{playerID}";

            if (string.IsNullOrEmpty(playerInfo.AvatarUrl))
            {
                QueueForOperation(operation.Generation, () =>
                {
                    Emoji.Register(sid, GFX.Gui["miaonet/missing_avatar"], 64, 64);
                    Emoji.Fill(MiaoNetFont.ENZhsFont);
                });
                return;
            }

            if (!Uri.TryCreate(playerInfo.AvatarUrl, UriKind.Absolute, out Uri? uri))
            {
                Logger.Warn(LT.MiaoNetAvatar, $"Invalid url \"{playerInfo.AvatarUrl}\" for player {playerInfo.DisplayName}.");
                QueueForOperation(operation.Generation, () =>
                {
                    Emoji.Register(sid, GFX.Gui["miaonet/missing_avatar"], 64, 64);
                    Emoji.Fill(MiaoNetFont.ENZhsFont);
                });
                return;
            }

            string avatarPath = await AvatarManager.GetAsync(uri).ConfigureAwait(false);

            QueueForOperation(operation.Generation, () =>
            {
                MTexture tex;
                try
                {
                    tex = new(VirtualContent.CreateTexture(avatarPath));
                }
                catch (Exception e)
                {
                    Logger.Error(LT.MiaoNetAvatar, $"Failed to create texture of \"{playerInfo.AvatarUrl}\" for player {playerInfo.DisplayName}");
                    Logger.LogDetailed(e);
                    tex = GFX.Gui["miaonet/missing_avatar"];
                }
                Emoji.Register(sid, tex, 64, 64);
                Emoji.Fill(MiaoNetFont.ENZhsFont);
            });
        }
        catch (Exception e)
        {
            Logger.Error(
                LT.MiaoNetAvatar,
                $"Error on avatar preparing for player \"{playerInfo}\" " +
                $"of id {playerID} with url {playerInfo.AvatarUrl}."
            );
            Logger.LogDetailed(e);
        }
    }

    private void QueueForOperation(long generation, Action action)
    {
        mainThreadQueue.Enqueue(() =>
        {
            if (connectionLifecycle.IsCurrent(generation))
                action();
        });
    }

    private void HandleQueuedPacket(IContextualPacket packet)
    {
        if (packet is PacketResponse response)
        {
            if (pendingRequests.TryRemove(response.RequestID, out var handler))
            {
                handler(response);
            }
            else
            {
                Logger.Warn(LT.MiaoNet, $"Unknown response id: {response.RequestID}.");
            }
        }
        else
        {
            bool handled = packetDispatcher.DispatchPacket(packet);
            if (!handled)
                Logger.Warn(LT.MiaoNet, $"Unhandled packet type: {packet.GetType()}.");
        }
    }

    public void Render()
    {
        BeginRender();
        try
        {
            if (HasConnection)
                renderableComponents.ForEach(c => c.Render());
            StatusComponent.Render();
        }
        catch (Exception e)
        {
            Logger.Error(LT.MiaoNet, "Exception occurred during rendering!");
            Logger.LogDetailed(e, LT.MiaoNet);
            DisconnectByException(e);
        }
        finally
        {
            EndRender();
        }
    }

    public static void BeginRender()
    {
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.LinearClamp,
            DepthStencilState.None,
            RasterizerState.CullNone,
            null,
            Engine.ScreenMatrix
        );
    }

    public static void EndRender()
    {
        Draw.SpriteBatch.End();
    }

    public void QueuePacket(IContextualPacket packet)
    {
        SafeGuard.Assert(HasConnection);
        connection.QueuePacket(packet);
    }

    private void EnqueueReceivedPacket(long generation, IContextualPacket packet)
    {
        PacketPriority priority = PacketPriorityClassifier.Classify(packet);
        receiveQueue.Enqueue(priority, new(generation, packet, Stopwatch.GetTimestamp()));
    }

    private bool TryDequeueReceivedPacket(
        out long generation,
        out IContextualPacket packet,
        out long enqueuedAt,
        bool includeWatchEntity = true
    )
    {
        bool dequeued = includeWatchEntity
            ? receiveQueue.TryDequeue(out ReceivedPacket received)
            : receiveQueue.TryDequeueNonEntity(out received);
        if (dequeued)
        {
            generation = received.Generation;
            packet = received.Packet;
            enqueuedAt = received.EnqueuedAt;
            return true;
        }
        generation = 0;
        packet = null!;
        enqueuedAt = 0;
        return false;
    }


    public void Request<TResponse>(PacketRequest<TResponse> request, Action<TResponse> callback)
        where TResponse : PacketResponse
        => Request(request, callback, CancellationToken.None);

    // TODO support cancelling request
    // or... do we actually need it?
    private void Request<TResponse>(
        PacketRequest<TResponse> packet, Action<TResponse> onResponse,
        CancellationToken token
    ) where TResponse : PacketResponse
    {
        _ = token;
        int id;
        packet.RequestID = id = Interlocked.Increment(ref currentRequestID);

        bool success = pendingRequests.TryAdd(id, (res) => onResponse((TResponse)res));
        SafeGuard.Assert(success);
        QueuePacket(packet);
    }

    public void Response<TResponse>(PacketRequest<TResponse> request, TResponse response)
        where TResponse : PacketResponse
    {
        response.RequestID = request.RequestID;
        QueuePacket(response);
    }

    [MemberNotNull(nameof(connection), nameof(ClientState), nameof(PlayerPresenceMessage))]
    private void EnsureState()
    {
        SafeGuard.Assert(HasConnection);
    }
}
