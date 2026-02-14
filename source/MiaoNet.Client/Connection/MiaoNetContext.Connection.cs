using System.Net;
using System.Net.Sockets;
using System.Text;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

partial class MiaoNetContext
{
    private void ConnectionThread(object? param)
    {
        var connectionToken = (CancellationToken)param!;

        if (connectionToken.IsCancellationRequested)
            return;

        SingleThreadedSynchronizationContext syncCtx = new();
        SingleThreadedTaskScheduler taskScheduler = new(syncCtx);
        SynchronizationContext.SetSynchronizationContext(syncCtx);

        CancellationTokenSource threadCts = new();
        _ = StartConnectionAsync(this, connectionToken).ContinueWith(t =>
        {
            threadCts.Cancel();
            if (t.IsFaulted)
            {
                Logger.Error(LT.MiaoNetConnection, "Unhandled exception in connection thread!");
                // throw to main thread
                mainThreadQueue.Enqueue(() => throw t.Exception);
            }
        });

        try
        {
            syncCtx.ProcessLoop(threadCts.Token);
        }
        catch (OperationCanceledException)
        {
            Logger.Info(LT.MiaoNetConnection, "Connection thread cancelled.");
            return;
        }
        finally
        {
            threadCts.Dispose();
        }

        Logger.Info(LT.MiaoNetConnection, "Connection thread exited.");
        return;

        async Task StartConnectionAsync(IPacketSerializationContext context, CancellationToken token)
        {
#if USE_CELEMIAO_AUTH
            if (MiaoNetModule.Settings.TokenData is null or { Length: 0 } && ClientRC.AuthenticationCode is null)
            {
                QueueDisconnectStatus(Dialog.Get("miaonet_connection_status_no_token"));
                return;
            }
#else
            if (string.IsNullOrEmpty(MiaoNetModule.Settings.Name))
            {
                QueueDisconnectStatus(Dialog.Get("miaonet_connection_status_no_name"));
                return;
            }
#endif

            string host = TargetServer;
            int Port = TargetPort;

            EndPoint ep = IPAddress.TryParse(host, out var ipa)
                ? new IPEndPoint(ipa, Port)
                : new DnsEndPoint(host, Port);

            byte langCode = 0;
            HandshakeData.NetMod[] netMods = [];

            HandshakeData handshakeData;

#if USE_CELEMIAO_AUTH
            if (ClientRC.AuthenticationCode is null)
            {
                Logger.Info(LT.MiaoNetConnection, "Using AuthType QuickLogin to log in.");
                handshakeData = new HandshakeData(langCode, AuthenticationType.QuickLogin, MiaoNetModule.Settings.TokenData!, netMods);
            }
            else
            {
                Logger.Info(LT.MiaoNetConnection, "Auth code is not null, using AuthType Authorize to log in.");
                handshakeData = new HandshakeData(langCode, AuthenticationType.Authorize, Encoding.UTF8.GetBytes(ClientRC.AuthenticationCode), netMods);
            }

            ClientRC.AuthenticationCode = null;
#else
            var settings = MiaoNetModule.Settings;
            string name = settings.Name;
            string? prefix = settings.Prefix;
            Color color = Calc.HexToColor(settings.Color);
            PlayerInfo playerInfo = new(name, prefix ?? string.Empty, string.Empty, color);
            MemoryStream ms = new(32);
            RefBinaryWriter writer = new(ms);
            writer.Write(playerInfo);
            byte[] authData = ms.GetBuffer().AsSpan(0, checked((int)ms.Position)).ToArray();
            handshakeData = new(langCode, AuthenticationType.QuickLogin, authData, netMods);
#endif

            MiaoServerConnection? connection = null;
            try
            {
                connection = await MiaoServerConnection.CreateAsync(ep, TargetServer, handshakeData, token);

                Version localVersion = MiaoNetModule.Instance.Metadata.Version;
                Version? version = await connection.MakeVersionCheck(localVersion, token);
                if (version is not null)
                {
                    connection.Dispose();
                    QueueDisconnectStatus(ConnectionStatus.VersionNotMatch(localVersion, version));
                    return;
                }
                else
                {
                    QueueStatus(ConnectionStatus.Authenticating);
                }

                HandshakeAckData handshakeAck = await connection.MakeHandshakeAsync(handshakeData, token);

                string? reason = handshakeAck.DeniedReason;
                if (reason is not null)
                {
                    connection.Dispose();
                    QueueDisconnectStatus(reason);
                    return;
                }

#if USE_CELEMIAO_AUTH
                if (handshakeAck.AuthenticationData is not null)
                {
                    MiaoNetModule.Settings.TokenData = handshakeAck.AuthenticationData;
                    Logger.Info(LT.MiaoNetConnection, "Server sent new auth data, accepted.");
                }
#endif

                IContextualPacket? packetInitial = await connection!.ReceivePacketAsync(context, token);
                if (packetInitial is not PacketClientInitial clientInitial)
                {
                    if (packetInitial is null)
                        Logger.Warn(LT.MiaoNetConnection, $"Remote sent empty or invalid initial reply.");
                    else
                        Logger.Warn(LT.MiaoNetConnection, $"Remote sent a weird initial packet {packetInitial.GetType()}.");
                    connection.Dispose();
                    QueueDisconnectStatus(ConnectionStatus.DisconnectedExceptionally);
                    return;
                }
                else
                {
                    Logger.Info(LT.MiaoNetConnection, $"Connected to {ep}.");

                    mainThreadQueue.Enqueue(() =>
                    {
#if USE_CELEMIAO_AUTH
                        MiaoNetModule.Settings.LastName = clientInitial.SelfPlayerInfo.Name;
#endif
                        clientState = new(clientInitial);
                        this.connection = connection;
                        ClientInitialized?.Invoke(clientState);
                        StatusComponent.ShowStatusMessage(ConnectionStatus.Connected);
                        OnConnected();
                    });
                }

            }
            catch (OperationCanceledException e)
            when (e.CancellationToken == token)
            {
                connection?.Dispose();
                Logger.Info(LT.MiaoNetConnection, "Connection cancelled");
                QueueDisconnectStatus(ConnectionStatus.Cancelled);
                return;
            }
            catch (Exception e)
            {
                connection?.Dispose();
                Logger.Error(LT.MiaoNetConnection, $"Error when connecting: {e}");
                QueueDisconnectStatus(ConnectionStatus.ConnectFailedWithReason(e.Message));
                return;
            }

            try
            {
                using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    Task receiveTask = ReceivePacketsLoopAsync(connection, context, cts.Token);
                    Task sendTask = connection.SendPacketsLoopAsync(context, cts.Token);

                    Task task = await Task.WhenAny(receiveTask, sendTask);
                    if (task.IsFaulted)
                        await task;
                    cts.Cancel();
                }
            }
            catch (OperationCanceledException e)
            when (e.CancellationToken == token)
            {
                Logger.Info(LT.MiaoNetConnection, "Connection cancelled");
                QueueDisconnectStatus(ConnectionStatus.Cancelled);
                return;
            }
            catch (Exception e)
            {
                Logger.Error(LT.MiaoNetConnection, $"Error during connection: {e}");
                if (e is IOException && e.InnerException is SocketException se)
                    e = se;
                QueueDisconnectStatus(ConnectionStatus.DisconnectedWithReason(e.Message));
                return;
            }
        }

        // TODO move to connection
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
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
#endif

            await Task.Yield();
            while (!token.IsCancellationRequested)
            {
                IContextualPacket? packet = await connection.ReceivePacketAsync(context, token);
                if (packet is null)
                    return;

                // quickly handle ping packets
                if (packet is PacketPing ping)
                {
                    // we may still don't have connection set this time
                    connection.QueuePacket(new PacketPong() { RequestID = ping.RequestID });
                    continue;
                }
#if PACKET_TRACING
                string typeName = packet.GetType().ToString();
                if (
                    !typeName.Contains("Frame")
                    && !typeName.Contains("PingData")
                    && !typeName.Contains("UpdateOnlineStatus")
                    && !typeName.Contains("PlayedAudio")
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

        void QueueDisconnectStatus(string statusMessage)
        {
            mainThreadQueue.Enqueue(() =>
            {
                StatusComponent.ShowStatusMessage(statusMessage);
                OnDisconnected();
            });
        }

        void QueueStatus(string statusMessage)
            => mainThreadQueue.Enqueue(() => StatusComponent.ShowStatusMessage(statusMessage, true));
    }
}
