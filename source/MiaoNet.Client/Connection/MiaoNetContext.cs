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

    private CancellationTokenSource? cts;
    private readonly ConcurrentQueue<IContextualPacket> receiveQueue;
    private readonly ConcurrentQueue<Action> mainThreadQueue;

    private readonly List<MiaoNetComponent> components;
    private readonly List<MiaoNetComponent> renderableComponents;
    private MiaoServerConnection? connection;
    private readonly PacketDispatcher packetDispatcher;

    private ClientState? clientState;

#if DEBUG
    public string TargetServer { get; set; } = "127.0.0.1";
#else
    public string TargetServer { get; set; } = "s.saplonily.top";
#endif

    public int TargetPort { get; set; } = 21473;

    public static bool IsSuitableToOpenUI
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
                   (scene as Level)?.Overlay == null;
#pragma warning restore IDE0260
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
        if (cts is not null)
            return;
        cts = new();
        Thread connectionThread = new(new ParameterizedThreadStart(ConnectionThread));
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
        if (connection is not null)
            StatusComponent.ShowStatusMessage(ConnectionStatus.Disconnected);
        OnDisconnected();
    }

    public void DisconnectByException(Exception exception)
    {
        StatusComponent.ShowStatusMessage(ConnectionStatus.DisconnectedWithLocalReason(exception.Message));
        OnDisconnected();
    }

    public void OnDisconnected()
    {
        cts?.Cancel();
        cts = null;
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
        AvatarManager.PersistStateToDisk();
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
                HandleQueuedPacket(packet);

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

    // warn: this is called on Connection Thread
    private bool HandleDirectPacket(IContextualPacket packet)
    {
        if (packet is PacketPing ping)
        {
            Response(ping, new PacketPong());
            return true;
        }
        else if (packet is PacketPlayerJoined joined)
        {
            SynchronizationContext.Current!.Post(async s =>
            {
                PacketPlayerJoined joined = (PacketPlayerJoined)s!;
                await SafePrepareAvatarAsync(joined.PlayerID, joined.PlayerInfo);
            }, joined);
        }
        return false;
    }

    private async Task SafePrepareAvatarAsync(int playerID, PlayerInfo playerInfo)
    {
        try
        {
            string sid = $"\0mn_avt_{playerID}";

            if (!Uri.TryCreate(playerInfo.AvatarUrl, UriKind.Absolute, out Uri? uri))
            {
                Logger.Warn(LT.MiaoNetAvatar, $"Invalid url \"{playerInfo.AvatarUrl}\" for player {playerInfo.DisplayName}.");
                mainThreadQueue.Enqueue(() =>
                {
                    Emoji.Register(sid, GFX.Gui["miaonet/missing_avatar"], 64, 64);
                    Emoji.Fill(MiaoNetFont.ENZhsFont);
                });
                return;
            }

            string avatarPath = await AvatarManager.GetAsync(uri);

            mainThreadQueue.Enqueue(() =>
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
            BlendState.NonPremultiplied,
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

    [MemberNotNull(nameof(connection), nameof(ClientState))]
    private void EnsureState()
    {
        SafeGuard.Assert(HasConnection);
    }
}