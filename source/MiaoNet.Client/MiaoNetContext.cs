using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MiaoNet.Shared;
using Monocle;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MiaoNetContext
{
    private CancellationTokenSource? cts;
    private Thread? connctionThread;
    private readonly ConcurrentQueue<IPacket> packetQueue;

    private readonly List<MiaoNetComponent> components;
    private MiaoServerConnection? connection;

    private readonly PacketDispatcher packetDispatcher;

    [MemberNotNullWhen(true, nameof(connection))]
    public bool HasConnection => connection is not null;

    public ClientState? ClientState { get; private set; }

    public MiaoNetMainComponent MainComponent { get; }

    public MiaoNetContext()
    {
        packetQueue = new();
        components = [
            MainComponent = new MiaoNetMainComponent(this)
        ];
        PacketHandlerRegister r = new();
        r.Register<PacketClientInitial>(HandlePacket);
        r.Register<PacketPlayerJoined>(HandlePacket);
        r.Register<PacketPlayerLeft>(HandlePacket);
        r.Register<PacketPlayerFrameNotify>(HandlePacket);
        r.Register<PacketPlayerMapChangedNotify>(HandlePacket);
        packetDispatcher = new(r);

#if DEBUG
        Engine.Instance.IsMouseVisible = true;
        if (GFX.Loaded)
            Task.Delay(500).ContinueWith(_ => Connect());
#endif
    }

    private void HandlePacket(PacketClientInitial packet)
    {
        PlayerLocationInfo locationInfo;
        if (Engine.Scene is Level level)
            locationInfo = new(level.Session.Area.SID, level.Session.Level);
        else
            locationInfo = new(string.Empty, string.Empty);

        ClientState = new(packet, locationInfo);
        ClientInitialized?.Invoke(ClientState);
    }

    private void HandlePacket(PacketPlayerJoined packet)
    {
        EnsureState();
        var player = ClientState.OnNewPlayerJoined(packet);
        PlayerJoined?.Invoke(player);
    }

    private void HandlePacket(PacketPlayerLeft packet)
    {
        EnsureState();
        PlayerLeft?.Invoke(ClientState.Players[packet.PlayerID]);
        ClientState.OnPlayerLeft(packet.PlayerID);
    }

    private void HandlePacket(PacketPlayerFrameNotify packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        PlayerFrameNotify?.Invoke(player, packet.Packet);
    }

    private void HandlePacket(PacketPlayerMapChangedNotify packet)
    {
        EnsureState();
        var player = ClientState.Players[packet.PlayerID];
        player.LocationInfo.UpdateWith(packet.MapSid, packet.MapRoom);
        PlayerMapChanged?.Invoke(player, packet);
    }

    public void Connect()
    {
        if (connctionThread is not null)
            return;
        cts = new();
        connctionThread = new(ConnectionThread);
        connctionThread.Start(cts.Token);
    }

    public void Disconnect()
    {
        cts?.Cancel();
        cts = null;
        connctionThread = null;
        packetQueue.Clear();
        ClientState = null;
        components.ForEach(c => c.OnDisconnected());
        if (connection is null)
            return;
        connection.Dispose();
        connection = null;
    }

    public void Update()
    {
        if (connection is null)
            return;
        while (TryTakePacket(out var packet))
        {
            if (!packetDispatcher.DispatchPacket(packet))
                Logger.Warn(nameof(MiaoNet), $"Unhandled packet type: {packet.GetType()}.");
        }
        components.ForEach(c => c.Update());
    }

    public void Render()
    {
        Draw.SpriteBatch.Begin();
        components.ForEach(c => c.Render());
        Draw.SpriteBatch.End();
    }

    public bool TryTakePacket([NotNullWhen(true)] out IPacket? packet)
        => packetQueue.TryDequeue(out packet);

    public void SendPacket(IPacket packet)
        => connection!.SendPacket(packet);

    private void ConnectionThread(object? tokenObject)
    {
        CancellationToken token = (CancellationToken)tokenObject!;
        if (token.IsCancellationRequested)
            return;

        try
        {
            IPEndPoint ipe = IPEndPoint.Parse("127.0.0.1:21473");
            HandshakeData handshakeData = new(MiaoNetModule.Instance.Metadata.Version, 0, MiaoNetModule.Settings.Name, []);
            connection = new(ipe, handshakeData);
            Logger.Info(nameof(MiaoNet), $"Connected to {ipe}.");
            components.ForEach(c => c.OnConnected());

            while (!token.IsCancellationRequested)
            {
                IPacket packet = connection.ReceivePacket();
                packetQueue.Enqueue(packet);
            }
            return;
        }
        catch (IOException e)
        when (e.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionAborted })
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

    [Conditional("DEBUG")]
    [MemberNotNull(nameof(ClientState))]
    private void EnsureState()
    {
        Debug.Assert(ClientState != null);
    }
}