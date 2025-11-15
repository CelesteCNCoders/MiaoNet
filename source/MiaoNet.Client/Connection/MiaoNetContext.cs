using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MiaoNetContext
{
    private CancellationTokenSource? cts;
    private Thread? connectionThread;
    private volatile bool justConnected;
    private readonly ConcurrentQueue<IPacket> receiveQueue;

    private readonly List<MiaoNetComponent> components;
    private MiaoServerConnection? connection;
    private readonly PacketDispatcher packetDispatcher;

    private ClientState? clientState;

    [MemberNotNullWhen(true, nameof(connection))]
    public bool HasConnection => connection is not null;

    [MemberNotNullWhen(true, nameof(ClientState))]
    public bool HasState => clientState is not null;

    public ClientState? ClientState => clientState;

    public MiaoNetMainComponent MainComponent { get; }

    public MiaoNetContext()
    {
        receiveQueue = new();
        components = [
            MainComponent = new MiaoNetMainComponent(this),
            new PlayerListComponent(this)
        ];
        PacketHandlerRegister r = new();
        r.Register<PacketClientInitial>(HandlePacket);
        r.Register<PacketPlayerJoined>(HandlePacket);
        r.Register<PacketPlayerLeft>(HandlePacket);
        r.Register<PacketPlayerFrameNotification>(HandlePacket);
        r.Register<PacketPlayerMapChangedNotification>(HandlePacket);
        r.Register<PacketPlayerMapRoomChangedNotification>(HandlePacket);
        r.Register<PacketPlayerMapChangedResponse>(HandlePacket);
        packetDispatcher = new(r);

#if DEBUG
        Engine.Instance.IsMouseVisible = true;
        if (GFX.Loaded)
            Task.Delay(500).ContinueWith(_ => Connect());
#endif
    }

    public void OnPlayerLocationChanged(Level level, PlayerLocation location)
    {
        if (!HasState)
            return;
        switch (ClientState.OnPlayerLocationChanged(location))
        {
        case PlayerLocation.ChangedResult.RoomOnly:
        {
            PacketPlayerMapRoomChanged p = new(location.MapRoom);
            QueuePacket(p);
            break;
        }
        case PlayerLocation.ChangedResult.All:
        {
            if (!TryGetAndSendSync(level, location))
                level.OnEndOfFrame += () =>
                {
                    bool result = TryGetAndSendSync(level, location);
                    SafeGuard.Assert(result);
                };

            bool TryGetAndSendSync(Level level, PlayerLocation location)
            {
                Player player = level.Tracker.GetEntity<Player>();
                if (player is null)
                    return false;
                // TODO move to main component
                PlayerState initialState = new PlayerState(player.X, player.Y, (byte)player.Dashes, Engine.DeltaTime);
                ClientState.Self.State = initialState;
                PacketPlayerMapChanged p = new(location, initialState);
                QueuePacket(p);
                return true;
            }
            break;
        }
        }
    }

    public void Connect()
    {
        if (connectionThread is not null)
            return;
        cts = new();
        connectionThread = new(ConnectionThread);
        connectionThread.Name = "MiaoNet Connection";
        connectionThread.Start(cts.Token);
    }

    public void Disconnect()
    {
        cts?.Cancel();
        cts = null;
        connectionThread = null;
        receiveQueue.Clear();
        clientState = null;
        components.ForEach(c => c.OnDisconnected());
        if (connection is null)
            return;
        connection.Dispose();
        connection = null;
    }

    public void Update()
    {
        if (!HasConnection)
            return;
        if (justConnected)
        {
            justConnected = false;
            components.ForEach(c => c.OnConnected());
        }
        while (TryTakePacket(out var packet))
        {
            if (!packetDispatcher.DispatchPacket(packet))
                Logger.Warn(nameof(MiaoNet), $"Unhandled packet type: {packet.GetType()}.");
        }
        if (HasState)
            components.ForEach(c => c.Update());
    }

    public void Render()
    {
        if (!HasState)
            return;
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
        components.ForEach(c => c.Render());
        Draw.SpriteBatch.End();
    }

    public bool TryTakePacket([NotNullWhen(true)] out IPacket? packet)
        => receiveQueue.TryDequeue(out packet);

    public void QueuePacket(IPacket packet)
    {
        SafeGuard.Assert(HasConnection);
        connection.QueuePacket(packet);
    }

    private void ConnectionThread(object? param)
    {
        var token = (CancellationToken)param!;

        if (token.IsCancellationRequested)
            return;

        SingleThreadedSynchronizationContext syncCtx = new();
        SynchronizationContext.SetSynchronizationContext(syncCtx);

        try
        {
            IPEndPoint ipe = IPEndPoint.Parse("127.0.0.1:21473");
            HandshakeData handshakeData = new(MiaoNetModule.Instance.Metadata.Version, 0, MiaoNetModule.Settings.Name, []);
            connection = new(ipe, handshakeData);
            Logger.Info(nameof(MiaoNet), $"Connected to {ipe}.");
            justConnected = true;

            _ = ReceivePacketsLoopAsync(token);
            _ = connection.SendPacketsLoopAsync(token);
            syncCtx.ProcessLoop(token);
            return;

            async Task ReceivePacketsLoopAsync(CancellationToken token)
            {
                await Task.Yield();
                while (!token.IsCancellationRequested)
                {
                    IPacket packet = await connection.ReceivePacketAsync();
                    receiveQueue.Enqueue(packet);
                }
            }
        }
        catch (IOException e)
        when (e.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionAborted })
        {
            Logger.Info(nameof(MiaoNet), "Connection aborted.");
        }
        catch (OperationCanceledException)
        {
            Logger.Info(nameof(MiaoNet), "Disconnected.");
        }
        catch (Exception e)
        {
            Logger.Error(nameof(MiaoNet), e.ToString());
        }
        finally
        {
            Disconnect();
        }
    }

    [MemberNotNull(nameof(ClientState), nameof(connection))]
    private void EnsureState()
    {
        SafeGuard.Assert(HasConnection);
        SafeGuard.Assert(HasState);
    }
}