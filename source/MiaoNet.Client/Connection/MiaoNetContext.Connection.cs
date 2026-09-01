using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MiaoNet.ClientShared;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

// TODO this is ugly, we need a refactor on this
partial class MiaoNetContext
{
    private sealed class ConnectionReceiveState
    {
        private readonly object sync = new();
        private readonly long generation;
        private readonly ConcurrentDictionary<int, byte> activePlayers = new();
        private readonly ConcurrentPacketPriorityQueue<ReceivedPacket> queue;
        private readonly WatchSceneTransferReceiver watchSceneTransferReceiver = new();
        private bool closed;

        internal ConnectionReceiveState(long generation)
        {
            this.generation = generation;
            queue = new(IsPlayerActive);
        }

        internal void InitializePlayers(PacketClientInitial initial)
        {
            lock (sync)
            {
                if (closed)
                    return;
                activePlayers.TryAdd(initial.PlayerID, 0);
                foreach (PacketClientInitial.Player player in initial.Players)
                    activePlayers.TryAdd(player.PlayerID, 0);
            }
        }

        internal void Enqueue(IContextualPacket packet, long enqueuedAt)
        {
            lock (sync)
            {
                if (closed)
                    return;

                if (packet is PacketPlayerJoined joined)
                    activePlayers.TryAdd(joined.PlayerID, 0);
                else if (packet is PacketPlayerLeft left)
                {
                    activePlayers.TryRemove(left.PlayerID, out _);
                    queue.ForgetPlayer(left.PlayerID);
                    watchSceneTransferReceiver.ClearForTarget(left.PlayerID);
                }

                if (packet is PacketPlayerLocationChangedNotification locationChanged)
                    watchSceneTransferReceiver.ClearForTarget(locationChanged.PlayerID);
                else if (packet is PacketPlayerChannelMovedNotification channelMoved)
                    watchSceneTransferReceiver.ClearForTarget(channelMoved.PlayerID);
                else if (packet is PacketWatchProducerStop
                    or PacketWatchEnded)
                    watchSceneTransferReceiver.ClearDiscardable();

                if (IsObsolete(packet))
                    return;

                if (watchSceneTransferReceiver.TryAccept(packet, out IContextualPacket? logicalPacket))
                {
                    if (logicalPacket is null)
                        return;
                    packet = logicalPacket;
                }

                queue.Enqueue(
                    PacketPriorityClassifier.Classify(packet),
                    new(generation, packet, enqueuedAt),
                    packet
                );
            }
        }

        internal bool TryDequeue(out ReceivedPacket packet, bool includeWatchEntity)
        {
            lock (sync)
            {
                if (closed)
                {
                    packet = default;
                    return false;
                }
                return includeWatchEntity
                    ? queue.TryDequeue(out packet)
                    : queue.TryDequeueNonEntity(out packet);
            }
        }

        internal IReadOnlyList<ReceivedPacket> CloseAndDrain()
        {
            lock (sync)
            {
                closed = true;
                watchSceneTransferReceiver.Clear();
                activePlayers.Clear();
                return queue.DrainAll();
            }
        }

        private bool IsPlayerActive(int playerID)
            => activePlayers.ContainsKey(playerID);

        private bool IsObsolete(IContextualPacket packet)
            => RemotePlayerPacket.GetInactiveDisposition(packet, out int playerID)
                == InactivePlayerPacketDisposition.Discard
                && !IsPlayerActive(playerID);
    }

    private sealed class ConnectionOperation : IPacketSerializationContext
    {
        private const int EndedFlag = 1;
        private const int ThreadCompletedFlag = 2;

        private readonly CancellationTokenSource cancellation = new();
        private MiaoServerConnection? ownedConnection;
        private int completionState;

        internal long Generation { get; }
        internal bool ShowAvatar { get; }
        internal string TargetServer { get; }
        internal int TargetPort { get; }
        internal CancellationToken Token => cancellation.Token;
        internal PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
        internal ConnectionReceiveState ReceiveState { get; }
#if USE_CELEMIAO_AUTH
        internal string? AuthenticationCode { get; }
        internal byte[]? TokenData { get; }
        internal byte[]? RefreshedAuthenticationData { get; set; }
#endif

        PooledStringManager IPacketSerializationContext.PooledStringManager => PooledStringManager;

        internal ConnectionOperation(long generation, bool showAvatar, string targetServer, int targetPort)
        {
            Generation = generation;
            ShowAvatar = showAvatar;
            TargetServer = targetServer;
            TargetPort = targetPort;
            ReceiveState = new(generation);
        }

#if USE_CELEMIAO_AUTH
        internal ConnectionOperation(
            long generation,
            bool showAvatar,
            string targetServer,
            int targetPort,
            string? authenticationCode,
            byte[]? tokenData
        ) : this(generation, showAvatar, targetServer, targetPort)
        {
            AuthenticationCode = authenticationCode;
            TokenData = tokenData;
        }
#endif

        internal void SetConnection(MiaoServerConnection connection)
        {
            if (Interlocked.CompareExchange(ref ownedConnection, connection, null) is not null)
                throw new InvalidOperationException("The operation already owns a connection.");
        }

        internal void Cancel()
        {
            cancellation.Cancel();
            MarkCompletion(EndedFlag);
        }

        internal void MarkThreadCompleted()
            => MarkCompletion(ThreadCompletedFlag);

        internal void CloseConnection(bool shutdown)
        {
            MiaoServerConnection? connection = Interlocked.Exchange(ref ownedConnection, null);
            connection?.Close(shutdown);
        }

        private void MarkCompletion(int flag)
        {
            int state = Interlocked.Or(ref completionState, flag) | flag;
            if (state == (EndedFlag | ThreadCompletedFlag))
                cancellation.Dispose();
        }
    }

    private void ConnectionThread(object? param)
    {
        var operation = (ConnectionOperation)param!;
        CancellationToken connectionToken = operation.Token;

        if (connectionToken.IsCancellationRequested)
        {
            operation.MarkThreadCompleted();
            return;
        }

        SingleThreadedSynchronizationContext syncCtx = new();
        SingleThreadedTaskScheduler taskScheduler = new(syncCtx);
        SynchronizationContext.SetSynchronizationContext(syncCtx);

        CancellationTokenSource threadCts = new();
        _ = StartConnectionAsync(operation, connectionToken).ContinueWith(t =>
        {
            threadCts.Cancel();
            if (t.IsFaulted)
            {
                Logger.Error(LT.MiaoNetConnection, "Unhandled exception in connection thread!");
                // throw to main thread
                QueueForOperation(operation.Generation, () => throw t.Exception!);
            }
        }, taskScheduler);

        try
        {
            syncCtx.ProcessLoop(threadCts.Token);
        }
        catch (OperationCanceledException e)
        when (e.CancellationToken == threadCts.Token)
        {
            Logger.Info(LT.MiaoNetConnection, "Connection thread cancelled.");
            return;
        }
        finally
        {
            threadCts.Dispose();
            operation.MarkThreadCompleted();
        }

        Logger.Info(LT.MiaoNetConnection, "Connection thread exited.");

        async Task StartConnectionAsync(ConnectionOperation operation, CancellationToken token)
        {
#if USE_CELEMIAO_AUTH
            if (operation.TokenData is null or { Length: 0 } && operation.AuthenticationCode is null)
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

            string host = operation.TargetServer;
            int Port = operation.TargetPort;

            EndPoint ep = IPAddress.TryParse(host, out var ipa)
                ? new IPEndPoint(ipa, Port)
                : new DnsEndPoint(host, Port);

            LanguageCode langCode = GameLanguage.GetLanguageCode(Dialog.Language.Id);
            HandshakeData.NetMod[] netMods = [];

            HandshakeData handshakeData;

#if USE_CELEMIAO_AUTH
            if (operation.AuthenticationCode is null)
            {
                handshakeData = new HandshakeData(langCode, false, operation.TokenData!, netMods);
            }
            else
            {
                Logger.Info(LT.MiaoNetConnection, "Auth code is not null, set isAuthorize to true to log in.");
                handshakeData = new HandshakeData(langCode, true, Encoding.UTF8.GetBytes(operation.AuthenticationCode), netMods);
            }
#else
            var settings = MiaoNetModule.Settings;
            string name = settings.Name;
            string? prefix = settings.Prefix;
            Color color = settings.Color is null ? Color.White : Calc.HexToColor(settings.Color);
            PlayerInfo playerInfo = new(-1, name, prefix ?? string.Empty, settings.AvatarUrl ?? string.Empty, color);
            MemoryStream ms = new(32);
            RefBinaryWriter writer = new(ms);
            writer.Write(playerInfo);
            byte[] authData = ms.GetBuffer().AsSpan(0, checked((int)ms.Position)).ToArray();
            handshakeData = new(langCode, false, authData, netMods);
#endif

            Logger.Info(LT.MiaoNetConnection, $"Trying connecting to {ep}...");
            MiaoServerConnection? connection = null;

            IAsyncEnumerator<IContextualPacket>? packetsAsyncEnumerator = null;
            using CancellationTokenSource sessionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            CancellationToken sessionToken = sessionCts.Token;
            try
            {
                bool revocationCheck = !MiaoNetModule.Settings.IgnoreCertRevocationStatus;
                connection = await MiaoServerConnection.CreateAsync(ep, operation.TargetServer, revocationCheck, token);
                operation.SetConnection(connection);

                Version localVersion = Connection.Version;
                Version? version = await connection.MakeVersionCheck(localVersion, token);
                if (version is not null)
                {
                    operation.CloseConnection(true);
                    QueueDisconnectStatus(ConnectionStatus.VersionNotMatch(localVersion, version));
                    return;
                }
                else
                {
                    QueueStatus(ConnectionStatus.Authenticating);
                }

                HandshakeAckData handshakeAck = await connection.MakeHandshakeAsync(handshakeData, token);
                var r = handshakeAck.AuthenticationResultType;
                if (r != AuthenticationResultType.Success)
                {
                    operation.CloseConnection(true);
                    string? reason = handshakeAck.DeniedReason;
                    string status = r switch
                    {
                        AuthenticationResultType.InvalidTokenData => ConnectionStatus.InvalidTokenData,
                        AuthenticationResultType.InternalServerError => ConnectionStatus.InternalServerError,
                        AuthenticationResultType.Suspended when reason is null => ConnectionStatus.Suspended,
                        _ => reason ?? ConnectionStatus.DisconnectedExceptionally,
                    };
                    QueueDisconnectStatus(status);
                    return;
                }

#if USE_CELEMIAO_AUTH
                if (handshakeAck.AuthenticationData is not null)
                    operation.RefreshedAuthenticationData = handshakeAck.AuthenticationData;
#endif

                packetsAsyncEnumerator = connection.ReceivePacketsLoopAsync(operation, sessionToken).GetAsyncEnumerator(sessionToken);

                await packetsAsyncEnumerator.MoveNextAsync();
                IContextualPacket? packetInitial = packetsAsyncEnumerator.Current;
                if (packetInitial is not PacketClientInitial clientInitial)
                {
                    if (packetInitial is null)
                        Logger.Warn(LT.MiaoNetConnection, $"Remote sent empty or invalid initial reply.");
                    else
                        Logger.Warn(LT.MiaoNetConnection, $"Remote sent a weird initial packet {packetInitial.GetType()}.");
                    await DisposePacketsAsync();
                    operation.CloseConnection(false);
                    QueueDisconnectStatus(ConnectionStatus.DisconnectedExceptionally);
                    return;
                }
                else
                {
                    Logger.Info(LT.MiaoNetConnection, $"Connected to {ep}.");
                    operation.ReceiveState.InitializePlayers(clientInitial);
                    TaskCompletionSource ackTaskSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    mainThreadQueue.Enqueue(() =>
                    {
                        try
                        {
                            if (connectionLifecycle.IsCurrent(operation.Generation))
                                OnInitialized(operation, connection, clientInitial);
                        }
                        finally
                        {
                            ackTaskSource.TrySetResult();
                        }
                    });
                    // wait until the main thread ack we've finished connecting
                    await ackTaskSource.Task.WaitAsync(token);
                    if (operation.ShowAvatar)
                    {
                        foreach (var p in clientInitial.Players)
                            _ = SafePrepareAvatarAsync(operation, p.PlayerID, p.PlayerInfo);
                        _ = SafePrepareAvatarAsync(operation, clientInitial.PlayerID, clientInitial.SelfPlayerInfo);
                    }
                }

            }
            catch (OperationCanceledException)
            when (token.IsCancellationRequested)
            {
                await DisposePacketsAsync();
                operation.CloseConnection(false);
                Logger.Info(LT.MiaoNetConnection, "Connection cancelled");
                QueueDisconnectStatus(ConnectionStatus.Cancelled);
                return;
            }
            catch (MiaoSslException e)
            {
                await DisposePacketsAsync();
                operation.CloseConnection(false);
                Logger.Error(LT.MiaoNetConnection, $"Ssl error: {e.SslPolicyErrors}. {e.X509ChainStatusFlags}");
                Logger.LogDetailed(e, LT.MiaoNetConnection);
                if (e.X509ChainStatusFlags.HasFlag(X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation))
                    QueueDisconnectStatus(ConnectionStatus.ConnectionSslRevocationCheckFailed);
                else
                    QueueDisconnectStatus(ConnectionStatus.ConnectionSslError(e.SslPolicyErrors, e.X509ChainStatusFlags));
                return;
            }
            catch (Exception e)
            {
                await DisposePacketsAsync();
                operation.CloseConnection(false);
                SocketException? se = (e as IOException)?.InnerException as SocketException;
                Logger.Error(LT.MiaoNetConnection, $"Error when connecting: {e}");
                QueueDisconnectStatus(ConnectionStatus.ConnectFailedWithReason((se ?? e).Message));
                return;
            }

            try
            {
                Task receiveTask = DoReceivingAndProcessingAsync(packetsAsyncEnumerator, operation, connection, sessionToken);
                Task sendTask = connection.SendPacketsLoopAsync(operation, sessionToken);

                await Task.WhenAny(receiveTask, sendTask);
                await sessionCts.CancelAsync();

                Exception? failure = null;
                foreach (Task task in new[] { receiveTask, sendTask })
                {
                    try
                    {
                        await task;
                    }
                    catch (OperationCanceledException) when (sessionCts.IsCancellationRequested)
                    {
                    }
                    catch (Exception e)
                    {
                        failure ??= e;
                    }
                }

                if (failure is not null)
                    throw failure;
                token.ThrowIfCancellationRequested();

                async Task DoReceivingAndProcessingAsync(
                    IAsyncEnumerator<IContextualPacket> packets,
                    ConnectionOperation operation,
                    MiaoServerConnection connection,
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
                    while (await packets.MoveNextAsync())
                    {
                        var packet = packets.Current;

                        if (!HandleDirectPacket(operation, connection, packet))
                            EnqueueReceivedPacket(operation, packet);
#if PACKET_TRACING
                        string typeName = packet.GetType().ToString();
                        if (
                            !typeName.Contains("Frame", StringComparison.Ordinal)
                            && !typeName.Contains("PingData", StringComparison.Ordinal)
                            && !typeName.Contains("UpdateOnlineStatus", StringComparison.Ordinal)
                            && !typeName.Contains("PlayedAudio", StringComparison.Ordinal)
                            && !typeName.Contains("PacketPing", StringComparison.Ordinal)
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
                    }
                }

                QueueDisconnectStatus(ConnectionStatus.Disconnected);
            }
            catch (OperationCanceledException)
            when (token.IsCancellationRequested)
            {
                Logger.Info(LT.MiaoNetConnection, "Connection cancelled.");
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
            finally
            {
                await DisposePacketsAsync();
            }

            async ValueTask DisposePacketsAsync()
            {
                if (packetsAsyncEnumerator is not null)
                {
                    await packetsAsyncEnumerator.DisposeAsync();
                    packetsAsyncEnumerator = null;
                }
            }
        }

        void QueueDisconnectStatus(string statusMessage)
        {
            QueueForOperation(operation.Generation, () =>
            {
                StatusComponent.ShowStatusMessage(statusMessage);
                OnDisconnected(operation.Generation);
            });
        }

        void QueueStatus(string statusMessage)
            => QueueForOperation(
                operation.Generation,
                () => StatusComponent.ShowStatusMessage(statusMessage, true)
            );
    }
}
