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
    private readonly ConcurrentQueue<IPacket> receiveQueue;
    private readonly ConcurrentQueue<Action> mainThreadQueue;

    private readonly List<MiaoNetComponent> components;
    private MiaoServerConnection? connection;
    private readonly PacketDispatcher packetDispatcher;

    private float statusMessageTimer;
    private string? statusMessage;

    private ClientState? clientState;

    [MemberNotNullWhen(true, nameof(connection))]
    [MemberNotNullWhen(true, nameof(ClientState))]
    public bool HasConnection => connection is not null;

    public ClientState? ClientState => clientState;

    public MainComponent MainComponent { get; }

    public MiaoNetContext()
    {
        receiveQueue = new();
        pendingRequests = new();
        mainThreadQueue = new();
        components = [
            MainComponent = new MainComponent(this),
            new PlayerListComponent(this),
            new ChatComponent(this),
            new DebugMapComponent(this),
            new EmoteComponent(this)
        ];
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
        ShowStatusMessage(MiaoNetConnectionStatus.Connecting);
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
        ShowStatusMessage(MiaoNetConnectionStatus.Disconnected);
    }

    public void ShowStatusMessage(string message)
    {
        statusMessageTimer = 2f;
        statusMessage = message;
    }

    public void ShowStatusMessage(MiaoNetConnectionStatus status)
    {
        ShowStatusMessage(status.ToString());
    }

    public void CleanUp()
    {

    }

    public void Update()
    {
        while (mainThreadQueue.TryDequeue(out var item))
            item();

        if (statusMessageTimer > 0f)
        {
            statusMessageTimer -= Engine.RawDeltaTime;
            if (statusMessageTimer <= 0f)
            {
                statusMessage = null;
            }
        }
        if (!HasConnection)
            return;
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
        components.ForEach(c => c.Update());
    }

    public void Render()
    {
        BeginRender();
        if (HasConnection)
            components.ForEach(c => c.Render());
        if (statusMessageTimer > 0f)
        {
            var tex = GFX.Gui["reloader/cogwheel"];
            Vector2 pos = new Vector2(64f, Engine.Height - 64f);
            const float Scale = 1f / 3.5f;
            tex.DrawOutlineJustified(pos, new Vector2(0f, 1f), Color.White, Scale);
            pos.X += tex.Width * Scale + 32f;
            MiaoNetFont.DrawStatusMessage(statusMessage!, pos);
        }
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

        StartConnectionAsync(token).ContinueWith(HandleTaskCompleted);

        try
        {
            syncCtx.ProcessLoop(token);
        }
        catch (OperationCanceledException)
        {
        }

        return;

        async Task StartConnectionAsync(CancellationToken token)
        {
            string host = "local.saplonily.top";

            EndPoint ep = IPAddress.TryParse(host, out var ipa)
                ? new IPEndPoint(ipa, 21473)
                : new DnsEndPoint(host, 21473);

            HandshakeData handshakeData = new(
                MiaoNetModule.Instance.Metadata.Version,
                0,
                MiaoNetModule.Settings.Name,
                []
            );
            MiaoServerConnection? connection;
            try
            {
                // this will send the full handshake, then we need to receive ack ourselves
                (connection, var ackData) = await MiaoServerConnection.CreateAsync(ep, handshakeData, token);

                if (ackData is null)
                {
                    Logger.Warn(nameof(MiaoNet), $"Remote sent empty or invalid reply.");
                    mainThreadQueue.Enqueue(() =>
                    {
                        ShowStatusMessage(MiaoNetConnectionStatus.Disconnected);
                        Disconnect();
                    });
                    return;
                }

                string? reason = ackData.DeniedReason;
                if (reason is not null)
                {
                    mainThreadQueue.Enqueue(() =>
                    {
                        ShowStatusMessage(reason);
                        Disconnect();
                    });
                    return;
                }
                else
                {
                    IPacket? packetInitial = await connection!.ReceivePacketAsync(token);
                    if (packetInitial is not PacketClientInitial clientInitial)
                    {
                        if (packetInitial is null)
                            Logger.Warn(nameof(MiaoNet), $"Remote sent empty or invalid initial reply.");
                        else
                            Logger.Warn(nameof(MiaoNet), $"Remote sent a werid {packetInitial.GetType()}.");
                        mainThreadQueue.Enqueue(() =>
                        {
                            ShowStatusMessage(MiaoNetConnectionStatus.Disconnected);
                            Disconnect();
                        });
                        return;
                    }
                    else
                    {
                        Logger.Info(nameof(MiaoNet), $"Connected to {ep}.");

                        this.connection = connection;
                        clientState = new(clientInitial);
                        mainThreadQueue.Enqueue(() =>
                        {
                            ClientInitialized?.Invoke(clientState);
                            ShowStatusMessage(MiaoNetConnectionStatus.Connected);
                            components.ForEach(c => c.OnConnected());
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(nameof(MiaoNet), $"Error when connecting: {e}");
                mainThreadQueue.Enqueue(() =>
                {
                    ShowStatusMessage($"Error when connecting.");
                    Disconnect();
                });
                return;
            }


            _ = ReceivePacketsLoopAsync(token).ContinueWith(HandleTaskCompleted, token);
            _ = connection.SendPacketsLoopAsync(token).ContinueWith(HandleTaskCompleted, token);
        }

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
                IPacket? packet = await connection!.ReceivePacketAsync(token);
                if (packet is null)
                    return;
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
            mainThreadQueue.Enqueue(() =>
            {
                ShowStatusMessage("Disconnected due to exception.");
                Disconnect();
            });

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

    [MemberNotNull(nameof(connection), nameof(ClientState))]
    private void EnsureState()
    {
        SafeGuard.Assert(HasConnection);
    }
}