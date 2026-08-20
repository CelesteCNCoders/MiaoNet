using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using MiaoNet.ClientShared;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MiaoNetContext : IPacketSerializationContext
{
    private int currentRequestID;
    // request id -> on response handler
    private readonly ConcurrentDictionary<int, Action<PacketResponse>> pendingRequests;
    //private int warningTimes;

    private readonly ConnectionLifecycleCoordinator connectionLifecycle;
    private ConnectionOperation? activeConnectionOperation;
    private readonly ConcurrentQueue<(long Generation, IContextualPacket Packet)> receiveQueue;
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
    public bool HasConnection => connection is not null;

    public ClientState? ClientState => clientState;

    public MainComponent MainComponent { get; }

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

        activeConnectionOperation = null;
        operation.Cancel();
        bool hadConnection = connection is not null;

        // any better ways?
        List<PacketDisconnected>? terminalPackets = null;
        while (receiveQueue.TryDequeue(out var received))
        {
            if (received.Generation == generation.Value && received.Packet is PacketDisconnected dc)
                (terminalPackets ??= []).Add(dc);
        }
        pendingRequests.Clear();
        clientState = null;
        hasComponentFocus = false;
        PooledStringManager = null;
        components?.ForEach(c => c.OnDisconnected());
        connection = null;
        operation.CloseConnection(false);
        if (hadConnection)
            AvatarManager.PersistStateToDisk();

        if (terminalPackets is not null)
        {
            foreach (PacketDisconnected packet in terminalPackets)
                packetDispatcher.DispatchPacket(packet);
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

            while (receiveQueue.TryDequeue(out var received))
            {
                if (connectionLifecycle.IsCurrent(received.Generation))
                    HandleQueuedPacket(received.Packet);
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
