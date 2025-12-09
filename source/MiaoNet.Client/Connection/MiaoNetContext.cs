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
    private int nextRequestID;
    // request id -> on response handler
    private readonly ConcurrentDictionary<int, Action<PacketResponse>> pendingRequests;

    //private int warningTimes;

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

    public MiaoNetConnectionStatus ConnectionStatus { get; private set; }

    public MainComponent MainComponent { get; }

    public MiaoNetContext()
    {
        receiveQueue = new();
        pendingRequests = new();
        components = [
            MainComponent = new MainComponent(this),
            new PlayerListComponent(this),
            new ChatComponent(this),
            new DebugMapComponent(this),
            new EmoteComponent(this)
        ];
        ConnectionStatus = MiaoNetConnectionStatus.Disconnected;
        PacketHandlerRegister r = new();
        RegisterPacketHandlers(r);
        packetDispatcher = new(r);

#if DEBUG
        Engine.Instance.IsMouseVisible = true;
        if (GFX.Loaded)
            Task.Delay(500).ContinueWith(_ => Connect());
#endif
    }

    public void Connect()
    {
        if (connectionThread is not null)
            return;
        cts = new();
        connectionThread = new(ConnectionThread);
        connectionThread.Name = "MiaoNet Connection";
        connectionThread.Start(cts.Token);
        ConnectionStatus = MiaoNetConnectionStatus.Connecting;
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

    public void CleanUp()
    {

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
                    Logger.Warn(nameof(MiaoNet), $"Unknown response id: {response.RequestID}. Is it the cancelled one?");
                }
            }
            else
            {
                bool handled = packetDispatcher.DispatchPacket(packet);
                if (!handled)
                    Logger.Warn(nameof(MiaoNet), $"Unhandled packet type: {packet.GetType()}.");
            }
        }
        if (HasState)
            components.ForEach(c => c.Update());
    }

    public void Render()
    {
        if (!HasState)
            return;
        BeginRender();
        components.ForEach(c => c.Render());
        EndRender();
    }

    public static void BeginRender(bool scissorEnabled = false)
    {
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.NonPremultiplied,
            SamplerState.LinearClamp,
            DepthStencilState.Default,
            scissorEnabled ? MiaoNetModule.ScissorEnabledRasterizerState : RasterizerState.CullNone,
            null,
            Engine.ScreenMatrix
        );
    }

    public static void EndRender()
    {
        Draw.SpriteBatch.End();
    }

    public void QueuePacket(IPacket packet)
    {
        SafeGuard.Assert(HasConnection);
        connection.QueuePacket(packet);
    }

    public void Request<TResponse>(PacketRequest<TResponse> packet, Action<TResponse> onResponse)
        where TResponse : PacketResponse
        => Request(packet, onResponse, CancellationToken.None);

    // TODO support cancelling request
    private void Request<TResponse>(
        PacketRequest<TResponse> packet, Action<TResponse> onResponse,
        CancellationToken token
    )
        where TResponse : PacketResponse
    {
        int id;
        packet.RequestID = id = Interlocked.Increment(ref nextRequestID);

        bool idConflict = pendingRequests.TryAdd(id, (res) => onResponse((TResponse)res));
        SafeGuard.Assert(!idConflict);
        QueuePacket(packet);
    }

    private void ConnectionThread(object? param)
    {
        var token = (CancellationToken)param!;

        if (token.IsCancellationRequested)
            return;

        SingleThreadedSynchronizationContext syncCtx = new();
        SynchronizationContext.SetSynchronizationContext(syncCtx);

        string host = "local.saplonily.top";
        EndPoint ep;
        if (IPAddress.TryParse(host, out var ipa))
            ep = new IPEndPoint(ipa, 21473);
        else
            ep = new DnsEndPoint(host, 21473);
        HandshakeData handshakeData = new(MiaoNetModule.Instance.Metadata.Version, 0, MiaoNetModule.Settings.Name, []);
        try
        {
            connection = new(ep, handshakeData);
        }
        catch (Exception e)
        {
            Logger.Error(nameof(MiaoNet), $"Error when connecting: {e}");
            Disconnect();
            return;
        }
        Logger.Info(nameof(MiaoNet), $"Connected to {ep}.");
        ConnectionStatus = MiaoNetConnectionStatus.Connected;
        justConnected = true;

        ReceivePacketsLoopAsync(token).ContinueWith(HandleTaskCompleted);
        connection.SendPacketsLoopAsync(token).ContinueWith(HandleTaskCompleted);
        try
        {
            syncCtx.ProcessLoop(token);
        }
        catch (OperationCanceledException)
        { }
        return;

        async Task ReceivePacketsLoopAsync(CancellationToken token)
        {
#if PACKET_TRACING
            System.Text.Json.JsonSerializerOptions options = new()
            {
                IncludeFields = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
            };
#endif

            await Task.Yield();
            while (!token.IsCancellationRequested)
            {
                IPacket packet = await connection.ReceivePacketAsync();
#if PACKET_TRACING
                if (!packet.GetType().ToString().Contains("Frame"))
                {
                    var pColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Type: {packet.GetType()}");
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize((object)packet, options));
                    Console.ForegroundColor = pColor;
                }
#endif
                receiveQueue.Enqueue(packet);
            }
        }

        void HandleTaskCompleted(Task t)
        {
            if (!t.IsFaulted)
                return;
            t.Exception.Handle(HandleTaskException);
            Disconnect();

            static bool HandleTaskException(Exception e)
            {
                switch (e)
                {
                case IOException
                when (e.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionAborted }):
                    Logger.Info(nameof(MiaoNet), "Connection aborted.");
                    break;

                case OperationCanceledException:
                    Logger.Info(nameof(MiaoNet), "Disconnected.");
                    break;

                default:
                    Logger.Error(nameof(MiaoNet), e.ToString());
                    break;
                }
                return true;
            }
        }
    }

    [MemberNotNull(nameof(ClientState), nameof(connection))]
    private void EnsureState()
    {
        SafeGuard.Assert(HasConnection);
        SafeGuard.Assert(HasState);
    }
}