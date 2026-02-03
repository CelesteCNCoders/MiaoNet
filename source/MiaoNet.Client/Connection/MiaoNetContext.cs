using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MiaoNetContext : IPacketSerializationContext
{
    private int currentRequestID;
    // request id -> on response handler
    private readonly ConcurrentDictionary<int, Action<PacketResponse>> pendingRequests;

    //private int warningTimes;
#if DEBUG
    public string TargetServer { get; set; } = "127.0.0.1";
#else
    public string TargetServer { get; set; } = "s.saplonily.top";
#endif

    public int TargetPort { get; set; } = 21474;

    private CancellationTokenSource? cts;
    private Thread? connectionThread;
    private readonly ConcurrentQueue<IContextualPacket> receiveQueue;
    private readonly ConcurrentQueue<Action> mainThreadQueue;

    private readonly List<MiaoNetComponent> components;
    private readonly List<MiaoNetComponent> renderableComponents;
    private MiaoServerConnection? connection;
    private readonly PacketDispatcher packetDispatcher;

    private ClientState? clientState;

    public static bool IsSuitableToOpenUI
    {
        get
        {
            var scene = Engine.Scene;
            return scene.Entities.Any(t => t is KeyboardConfigUI or ButtonConfigUI) == false &&
                   // do not open ui when it's teleporting using CollabLobbyUI
                   // but why level.Overlay is null at this time??
                   scene is not LevelLoader &&
                   (scene as Level)?.Overlay == null;
        }
    }

    public PooledStringManager? PooledStringManager { get; private set; }

    PooledStringManager IPacketSerializationContext.PooledStringManager
    {
        get { EnsureState(); return PooledStringManager!; } // TODO connection status
    }

    [MemberNotNullWhen(true, nameof(connection), nameof(ClientState))]
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

    public void Connect()
    {
        if (connectionThread is not null)
            return;
        cts = new();
        connectionThread = new(ConnectionThread);
        connectionThread.Name = "MiaoNet Connection";
        connectionThread.Start(cts.Token);
        StatusComponent.ShowStatusMessage(ConnectionStatus.Connecting, true);
    }

    public void OnConnected()
    {
        PooledStringManager = new(KnownPooledStrings.All);
        components.ForEach(c => c.OnConnected());
    }

    public void Disconnect()
    {
        cts?.Cancel();
        cts = null;
        if (connection is not null)
        {
            connection.Dispose();
            connection = null;
            StatusComponent.ShowStatusMessage(ConnectionStatus.Disconnected);
        }
        OnDisconnected();
    }

    public void OnDisconnected()
    {
        cts?.Cancel();
        cts = null;
        connectionThread = null;
        // any better ways?
        while (receiveQueue.TryDequeue(out var packet))
        {
            if (packet is PacketDisconnected dc)
                packetDispatcher.DispatchPacket(dc);
        }
        receiveQueue.Clear();
        pendingRequests.Clear();
        clientState = null;
        PooledStringManager = null;
        components?.ForEach(c => c.OnDisconnected());
        if (connection is null)
            return;
        connection.Dispose();
        connection = null;
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

            while (receiveQueue.TryDequeue(out var packet))
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

            if (!HasConnection)
                return;

            components.ForEach(c => c.Update());
        }
        catch (Exception e)
        {
            Logger.LogDetailed(e, LT.MiaoNet);
            Disconnect();
        }
    }

    public void Render()
    {
        BeginRender();
        if (HasConnection)
            renderableComponents.ForEach(c => c.Render());
        StatusComponent.Render();
        EndRender();
    }

    public static void BeginRender()
    {
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.NonPremultiplied,
            SamplerState.LinearClamp,
            DepthStencilState.Default,
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

    [MemberNotNull(nameof(connection), nameof(ClientState))]
    private void EnsureState()
    {
        SafeGuard.Assert(HasConnection);
    }
}