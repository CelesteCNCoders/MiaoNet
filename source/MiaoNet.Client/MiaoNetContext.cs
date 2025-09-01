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

    public OnlineContext? OnlineContext { get; private set; }

    public OnlineChannel? CurrentChannel { get; private set; }

    public OnlinePlayer? Self { get; private set; }

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
        PlayerStateInfo stateInfo;
        if (Engine.Scene is Level level)
            stateInfo = new(level.Session.Area.SID, level.Session.Level);
        else
            stateInfo = new(string.Empty, string.Empty);
        var selfChannelStateInfo = new ChannelPlayerStateInfo(packet.ChannelID, packet.SelfPlayerInfo, stateInfo);
        OnlineContext = new(packet.Channels.Single(c => c.ID == packet.ChannelID), selfChannelStateInfo);
        Self = OnlineContext.Self;
        foreach (var player in packet.Players)
            OnlineContext.AddPlayer(player);
        CurrentChannel = OnlineContext.Channels[0];
        Debug.Assert(CurrentChannel.ID == 0);
        ClientInitialized?.Invoke(OnlineContext);
    }

    private void HandlePacket(PacketPlayerJoined packet)
    {
        EnsureState();
        var player = OnlineContext.AddPlayer(packet.Info);
        PlayerJoined?.Invoke(player);
    }

    private void HandlePacket(PacketPlayerLeft packet)
    {
        EnsureState();
        PlayerLeft?.Invoke(OnlineContext.GetPlayer(packet.PlayerID));
        OnlineContext.RemovePlayer(packet.PlayerID);
    }

    private void HandlePacket(PacketPlayerFrameNotify packet)
    {
        EnsureState();
        var player = OnlineContext.GetPlayer(packet.PlayerID);
        PlayerFrameNotify?.Invoke(player, packet.Packet);
    }

    private void HandlePacket(PacketPlayerMapChangedNotify packet)
    {
        EnsureState();
        var player = OnlineContext.GetPlayer(packet.PlayerID);
        string pMapSid = player.StateInfo.MapSid;
        string pMapRoom = player.StateInfo.MapRoom;
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
        OnlineContext = null;
        CurrentChannel = null;
        Self = null;
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
                Packet packet = connection.ReceivePacket();
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
    [MemberNotNull(nameof(OnlineContext), nameof(Self), nameof(CurrentChannel))]
    private void EnsureState()
    {
        Debug.Assert(OnlineContext != null);
        Debug.Assert(Self != null);
        Debug.Assert(CurrentChannel != null);
    }
}