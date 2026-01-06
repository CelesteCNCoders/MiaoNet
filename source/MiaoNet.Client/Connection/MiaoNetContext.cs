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

    private CancellationTokenSource? cts;
    private Thread? connectionThread;
    private readonly ConcurrentQueue<IContextualPacket> receiveQueue;
    private readonly ConcurrentQueue<Action> mainThreadQueue;

    private List<MiaoNetComponent> components;
    private MiaoServerConnection? connection;
    private readonly PacketDispatcher packetDispatcher;

    private ClientState? clientState;

    public static bool IsSuitableToOpenUI =>
        Engine.Scene.Tracker.GetEntity<KeyboardConfigUI>() == null &&
        Engine.Scene.Tracker.GetEntity<ButtonConfigUI>() == null;

    public PooledStringManager? PooledStringManager { get; private set; }

    PooledStringManager IPacketSerializationContext.PooledStringManager
    {
        get { EnsureState(); return PooledStringManager!; } // TODO connection status
    }

    [MemberNotNullWhen(true, nameof(connection), nameof(ClientState))]
    public bool HasConnection => connection is not null;

    public ClientState? ClientState => clientState;

    public MainComponent MainComponent { get; private set; }

    public EmoteComponent EmoteComponent { get; private set; }

    public ChatComponent ChatComponent { get; private set; }

    public StatusComponent StatusComponent { get; private set; }

    public MiaoNetContext()
    {
        receiveQueue = new();
        pendingRequests = new();
        mainThreadQueue = new();

        // any better ways?
        // will fill the first time connect
        components = null!;
        MainComponent = null!;
        EmoteComponent = null!;
        ChatComponent = null!;

        StatusComponent = new(this);
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
        components ??= [
            MainComponent = new MainComponent(this),
            new PlayerListComponent(this),
            ChatComponent = new ChatComponent(this),
            new DebugMapComponent(this),
            EmoteComponent = new EmoteComponent(this)
        ];

        if (connectionThread is not null)
            return;
        cts = new();
        connectionThread = new(ConnectionThread);
        connectionThread.Name = "MiaoNet Connection";
        connectionThread.Start(cts.Token);
        StatusComponent.ShowStatusMessage(MiaoNetConnectionStatus.Connecting);
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
            StatusComponent.ShowStatusMessage(MiaoNetConnectionStatus.Disconnected);
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
                        Logger.Warn(nameof(MiaoNet), $"Unknown response id: {response.RequestID}.");
                    }
                }
                else
                {
                    bool handled = packetDispatcher.DispatchPacket(packet);
                    if (!handled)
                        Logger.Warn(nameof(MiaoNet), $"Unhandled packet type: {packet.GetType()}.");
                }
            }

            if (!HasConnection)
                return;

            components.ForEach(c => c.Update());
        }
        catch (Exception e)
        {
            Logger.LogDetailed(e, nameof(MiaoNet));
            Disconnect();
        }
    }

    public void Render()
    {
        BeginRender();
        if (HasConnection)
            components.ForEach(c => c.Render());
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

    private void ConnectionThread(object? param)
    {
        var connectionToken = (CancellationToken)param!;

        if (connectionToken.IsCancellationRequested)
            return;

        SingleThreadedSynchronizationContext syncCtx = new();
        SynchronizationContext.SetSynchronizationContext(syncCtx);

        CancellationTokenSource threadCts = new();
        StartConnectionAsync(this, connectionToken).ContinueWith(t => HandleStartConnectionTaskCompleted(t, threadCts));

        try
        {
            syncCtx.ProcessLoop(threadCts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        return;

        async Task StartConnectionAsync(IPacketSerializationContext context, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(MiaoNetModule.Settings.Name))
            {
                mainThreadQueue.Enqueue(() =>
                {
                    StatusComponent.ShowStatusMessage("No name");
                    OnDisconnected();
                });
                return;
            }

            string host = TargetServer;
            const int Port = 21473;

            EndPoint ep = IPAddress.TryParse(host, out var ipa)
                ? new IPEndPoint(ipa, Port)
                : new DnsEndPoint(host, Port);


            HandshakeData handshakeData = new(
                MiaoNetModule.Instance.Metadata.Version,
                0,
                MiaoNetModule.Settings.Name,
                []
            );
            MiaoServerConnection? connection;
            try
            {
                // TODO maybe we should move these "connection stuffs" into the real
                // MiaoServerConnection "connection" class

                // this will send the full handshake, then we need to handle ack ourselves
                (connection, var ackData) = await MiaoServerConnection.CreateAsync(ep, handshakeData, token);

                if (ackData is null)
                {
                    Logger.Warn(nameof(MiaoNet), $"Remote sent empty or invalid reply.");
                    mainThreadQueue.Enqueue(() =>
                    {
                        StatusComponent.ShowStatusMessage(MiaoNetConnectionStatus.Disconnected);
                        OnDisconnected();
                    });
                    return;
                }

                string? reason = ackData.DeniedReason;
                if (reason is not null)
                {
                    mainThreadQueue.Enqueue(() =>
                    {
                        StatusComponent.ShowStatusMessage(reason);
                        OnDisconnected();
                    });
                    return;
                }
                else
                {
                    IContextualPacket? packetInitial = await connection!.ReceivePacketAsync(context, token);
                    if (packetInitial is not PacketClientInitial clientInitial)
                    {
                        if (packetInitial is null)
                            Logger.Warn(nameof(MiaoNet), $"Remote sent empty or invalid initial reply.");
                        else
                            Logger.Warn(nameof(MiaoNet), $"Remote sent a werid initial packet {packetInitial.GetType()}.");
                        mainThreadQueue.Enqueue(() =>
                        {
                            StatusComponent.ShowStatusMessage(MiaoNetConnectionStatus.Disconnected);
                            OnDisconnected();
                        });
                        return;
                    }
                    else
                    {
                        Logger.Info(nameof(MiaoNet), $"Connected to {ep}.");

                        mainThreadQueue.Enqueue(() =>
                        {
                            clientState = new(clientInitial);
                            this.connection = connection;
                            ClientInitialized?.Invoke(clientState);
                            StatusComponent.ShowStatusMessage(MiaoNetConnectionStatus.Connected);
                            OnConnected();
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Logger.Error(nameof(MiaoNet), $"Error when connecting: {e}");
                mainThreadQueue.Enqueue(() =>
                {
                    StatusComponent.ShowStatusMessage(
                        MiaoNetConnectionStatus.ConnectFailedWithException,
                        e.Message
                    );
                    OnDisconnected();
                });
                return;
            }

            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task receiveTask = ReceivePacketsLoopAsync(connection, context, cts.Token);
            Task sendTask = connection.SendPacketsLoopAsync(context, cts.Token);

            Task task = await Task.WhenAny(receiveTask, sendTask);
            if (task.IsFaulted)
                await task;
            cts.Cancel();
        }

        async Task ReceivePacketsLoopAsync(
            MiaoServerConnection connection,
            IPacketSerializationContext context,
            CancellationToken token
        )
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
                IContextualPacket? packet = await connection.ReceivePacketAsync(context, token);
                if (packet is null)
                    return;

                // quickly handle ping packets
                if (packet is PacketPing ping && HasConnection)
                {
                    Response(ping, new PacketPong());
                    continue;
                }
#if PACKET_TRACING
                string typeName = packet.GetType().ToString();
                if (
                    !typeName.Contains("Frame")
                    && !typeName.Contains("PingData")
                    && !typeName.Contains("UpdateOnlineStatus")
                )
                {
                    var pColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"== Type: {packet.GetType()} ==");
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize((object)packet, options));
                    Console.ForegroundColor = pColor;
                }
#endif
                receiveQueue.Enqueue(packet);
            }
        }

        void HandleStartConnectionTaskCompleted(Task t, CancellationTokenSource threadCts)
        {
            threadCts.Cancel();
            if (!t.IsFaulted)
            {
                if (t.IsCanceled)
                {
                    mainThreadQueue.Enqueue(() =>
                    {
                        StatusComponent.ShowStatusMessage(MiaoNetConnectionStatus.Cancelled);
                        OnDisconnected();
                    });
                }
                else
                {
                    mainThreadQueue.Enqueue(() =>
                    {
                        OnDisconnected();
                    });
                }
                return;
            }

            bool isExpected = true;
            Exception? unhandledException = null;

            foreach (var e in t.Exception.InnerExceptions)
            {
                switch (e)
                {
                case IOException when (e.InnerException is SocketException
                {
                    SocketErrorCode: SocketError.ConnectionAborted
                        or SocketError.ConnectionReset
                } se):
                    Logger.Info(nameof(MiaoNet), "Connection aborted.");
                    isExpected = false;
                    break;

                case OperationCanceledException:
                    Logger.Info(nameof(MiaoNet), "Disconnected.");
                    break;

                default:
                    Logger.Error(nameof(MiaoNet), e.ToString());
                    unhandledException = e;
                    isExpected = false;
                    break;
                }
            }

            if (!isExpected)
            {
                if (unhandledException is not null)
                {
                    mainThreadQueue.Enqueue(() =>
                    {
                        StatusComponent.ShowStatusMessage(
                            MiaoNetConnectionStatus.ConnectionAbortedWithException,
                            unhandledException.Message
                        );
                        OnDisconnected();
                    });
                }
                else
                {
                    mainThreadQueue.Enqueue(() =>
                    {
                        StatusComponent.ShowStatusMessage(
                            MiaoNetConnectionStatus.ConnectionAborted
                        );
                        OnDisconnected();
                    });
                }
            }
            else
            {
                mainThreadQueue.Enqueue(() =>
                {
                    StatusComponent.ShowStatusMessage(MiaoNetConnectionStatus.Disconnected);
                    OnDisconnected();
                });
            }
        }
    }

    [MemberNotNull(nameof(connection), nameof(ClientState))]
    private void EnsureState()
    {
        SafeGuard.Assert(HasConnection);
    }
}