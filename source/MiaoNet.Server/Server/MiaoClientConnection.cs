using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Channels;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

[DebuggerDisplay("ID = {ID}, Player = {Player}")]
public sealed class MiaoClientConnection : IPacketSerializationContext
{
    public const int TcpBufferSize = 2048;
    public const int PacketChannelSize = 256;
    public const int ControlPacketChannelSize = 32;
    public const int PlayerFramePacketChannelSize = 128;
    public const int WatchEntityPacketChannelSize = 64;
    public const int MaxPendingRequests = 64;

    public delegate Task ResponseHandler(PacketResponse response);
    public delegate Task ResponseHandler<in TResponse>(TResponse response) where TResponse : PacketResponse;

    private int currentRequestID;
    private sealed class PendingRequest(
        ResponseHandler handler,
        Func<Task>? timeoutHandler,
        CancellationTokenSource cancellationTokenSource
    ) : IDisposable
    {
        public ResponseHandler Handler { get; } = handler;
        public Func<Task>? TimeoutHandler { get; } = timeoutHandler;
        public CancellationTokenSource CancellationTokenSource { get; } = cancellationTokenSource;

        public void Dispose() => CancellationTokenSource.Dispose();
    }

    private readonly ConcurrentDictionary<int, PendingRequest> pendingRequests;
    private int pendingRequestCount;

    private readonly ILogger<MiaoClientConnection> logger;
    private readonly MiaoServerService server;
    private readonly MiaoMetricsService metricsService;

    private readonly INetworkConnection networkConnection;
    private readonly CancellationTokenSource cts;
    private readonly Pipe pipe;

    public int ID { get; }

    public ServerPlayer Player { get; }

    public PooledStringManager PooledStringManager { get; }

    private readonly PriorityPacketChannel sendChannel;
    private int outgoingQueueOverflowed;

    // TODO refactor
    public MiaoClientConnection(
        INetworkConnection networkConnection,
        ServerPlayer serverPlayer,
        ILogger<MiaoClientConnection> logger,
        MiaoServerService server,
        MiaoMetricsService metricsService
    )
    {
        this.logger = logger;
        this.server = server;
        this.metricsService = metricsService;
        this.networkConnection = networkConnection;
        ID = serverPlayer.ID;
        Player = serverPlayer;

        cts = new CancellationTokenSource();
        pipe = new();
        pendingRequests = new();

        sendChannel = new(
            ControlPacketChannelSize,
            PlayerFramePacketChannelSize,
            PacketChannelSize,
            WatchEntityPacketChannelSize
        );
        PooledStringManager = new(KnownPooledStrings.All);
    }

    public async Task HandleClientConnectAsync()
    {
        var token = cts.Token;
        Task receivingTask = HandleClientReceivingAsync(token);
        Task sendingTask = HandleClientSendingAsync(token);
        Task processingTask = HandleClientProcessingAsync(token);

        try
        {
            await Task.WhenAny(receivingTask, processingTask, sendingTask);
            await cts.CancelAsync();
            await Task.WhenAll(receivingTask, processingTask, sendingTask);
        }
        catch (IOException ioe)
        when (ioe.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted } e)
        {
            logger.LogInformation(AppEvents.Connection, "Connection aborted, id {id}.", ID);
        }
        catch (OperationCanceledException)
        {
            networkConnection.Shutdown();
            logger.LogDebug(AppEvents.Connection, "Connection id {id} handling cancelled.", ID);
        }
        catch (Exception e)
        {
            logger.LogError(AppEvents.Connection, e, "Exception when handling connection id {id}.", ID);
        }
        finally
        {
            sendChannel.Complete();
            await CancelPendingRequestsAsync();
            networkConnection.Dispose();
            logger.LogInformation(AppEvents.Connection, "Connection id {id} closed.", ID);
        }
    }

    public async Task DisconnectAsync(DisconnectReason reason, string? message = null)
    {
        cts.CancelAfter(server.DisconnectTimeout);
        await QueuePacketAsync(new PacketDisconnected(reason, message));
    }

    #region Packet

    public ValueTask QueuePacketAsync(IContextualPacket packet)
    {
        if (sendChannel.TryWrite(packet))
            return ValueTask.CompletedTask;
        if (PacketPriorityClassifier.Classify(packet) == PacketPriority.WatchEntity)
        {
            DisconnectSlowClient();
            return ValueTask.CompletedTask;
        }
        return WaitToQueuePacketAsync(packet);
    }

    private async ValueTask WaitToQueuePacketAsync(IContextualPacket packet)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        timeout.CancelAfter(server.DisconnectTimeout);
        try
        {
            await sendChannel.WriteAsync(packet, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!cts.IsCancellationRequested)
            {
                logger.LogWarning(
                    AppEvents.Connection,
                    "Disconnecting slow client {id}: outgoing packet queue stayed full.",
                    ID
                );
                await cts.CancelAsync();
            }
        }
        catch (ChannelClosedException)
        {
            // The connection is already shutting down; there is no receiver for this packet.
        }
    }

    private void DisconnectSlowClient()
    {
        if (Interlocked.Exchange(ref outgoingQueueOverflowed, 1) != 0)
            return;

        logger.LogWarning(
            AppEvents.Connection,
            "Disconnecting slow client {id}: outgoing Watch entity queue stayed full.",
            ID
        );
        cts.Cancel();
    }

    public bool TryQueuePacket(IContextualPacket packet)
        => sendChannel.TryWrite(packet);

    // TODO maybe we can add a UserParam parameter to avoid closure
    public async ValueTask<bool> RequestAsync<TResponse>(
        PacketRequest<TResponse> packet,
        ResponseHandler<TResponse> callback,
        TimeSpan timeout,
        Func<Task>? timeoutHandler = null,
        CancellationToken cancellationToken = default
    )
        where TResponse : PacketResponse
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        if (Interlocked.Increment(ref pendingRequestCount) > MaxPendingRequests)
        {
            Interlocked.Decrement(ref pendingRequestCount);
            return false;
        }

        int id = Interlocked.Increment(ref currentRequestID);
        packet.RequestID = id;
        CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        PendingRequest pending = new(
            response => callback((TResponse)response),
            timeoutHandler,
            timeoutSource
        );
        if (!pendingRequests.TryAdd(id, pending))
        {
            Interlocked.Decrement(ref pendingRequestCount);
            pending.Dispose();
            throw new InvalidOperationException($"Duplicate request id {id}.");
        }

        _ = ExpireRequestAsync(id, pending, timeout);
        await QueuePacketAsync(packet);
        return true;
    }

    public ValueTask ResponseAsync<TResponse>(PacketRequest<TResponse> request, TResponse response)
        where TResponse : PacketResponse
    {
        response.RequestID = request.RequestID;
        return QueuePacketAsync(response);
    }

    public ResponseHandler? OnResponse(PacketResponse response)
    {
        if (TryTakePendingRequest(response.RequestID, out var pending))
        {
            pending.CancellationTokenSource.Cancel();
            ResponseHandler handler = pending.Handler;
            pending.Dispose();
            return handler;
        }

        logger.LogWarning(
            "Could not find source request id of response {id}, type is {type}.",
            response.RequestID,
            response.GetType().FullName
        );
        foreach (var item in pendingRequests)
            logger.LogWarning("pendingRequests has key: {key}", item.Key);

        return null;
    }

    private async Task ExpireRequestAsync(int id, PendingRequest pending, TimeSpan timeout)
    {
        try
        {
            await Task.Delay(timeout, pending.CancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            if (TryTakePendingRequest(id, out var cancelled))
                cancelled.Dispose();
            return;
        }

        if (!TryTakePendingRequest(id, out var expired))
            return;

        Func<Task>? timeoutHandler = expired.TimeoutHandler;
        expired.Dispose();
        if (timeoutHandler is null)
            return;

        try
        {
            await timeoutHandler();
        }
        catch (Exception e)
        {
            logger.LogError(AppEvents.Connection, e, "Request {id} timeout handler failed for connection {connectionId}.", id, ID);
        }
    }

    private bool TryTakePendingRequest(int id, [NotNullWhen(true)] out PendingRequest? pending)
    {
        if (pendingRequests.TryRemove(id, out pending))
        {
            Interlocked.Decrement(ref pendingRequestCount);
            return true;
        }
        return false;
    }

    private async Task CancelPendingRequestsAsync()
    {
        foreach (int id in pendingRequests.Keys)
        {
            if (TryTakePendingRequest(id, out var pending))
            {
                Func<Task>? timeoutHandler = pending.TimeoutHandler;
                pending.CancellationTokenSource.Cancel();
                pending.Dispose();
                if (timeoutHandler is not null)
                {
                    try
                    {
                        await timeoutHandler();
                    }
                    catch (Exception e)
                    {
                        logger.LogError(
                            AppEvents.Connection,
                            e,
                            "Request {id} cancellation handler failed for connection {connectionId}.",
                            id,
                            ID
                        );
                    }
                }
            }
        }
    }

    #endregion

    private async Task HandleClientReceivingAsync(CancellationToken token)
    {
        var pipeWriter = pipe.Writer;
        while (true)
        {
            var mem = pipeWriter.GetMemory(TcpBufferSize);
            int received = await networkConnection.Stream.ReadAsync(mem, token);
            if (received is 0 || token.IsCancellationRequested)
                break;

            pipeWriter.Advance(received);

            FlushResult flushResult = await pipeWriter.FlushAsync(token);
            if (flushResult.IsCompleted)
                break;
        }
        await pipeWriter.CompleteAsync();
        logger.LogDebug("Receiving task of id {id} finished.", ID);
    }

    private async Task HandleClientProcessingAsync(CancellationToken token)
    {
        long leftoverBytes = await ProcessPacketsAsync(
            pipe.Reader,
            this,
            async (packet, bytesConsumed) =>
            {
                metricsService.RecordPacketTcpDownload(1, bytesConsumed);
                await server.HandlePacketAsync(this, packet);
            },
            token
        );
        if (leftoverBytes > 0)
        {
            logger.LogWarning(
                AppEvents.Connection,
                "Connection id {id} closed with {leftover} leftover bytes that do not form a complete packet frame.",
                ID,
                leftoverBytes
            );
        }
        logger.LogDebug("Processing task of id {id} finished.", ID);
    }

    internal static async Task<long> ProcessPacketsAsync(
        PipeReader pipeReader,
        IPacketSerializationContext context,
        Func<IContextualPacket, int, ValueTask> packetHandler,
        CancellationToken token
    )
    {
        try
        {
            long leftoverBytes = 0;
            while (true)
            {
                ReadResult result = await pipeReader.ReadAsync(token);
                ReadOnlySequence<byte> buffer = result.Buffer;
                while (true)
                {
                    long lengthBeforePacket = buffer.Length;
                    if (!TryParsePacket(ref buffer, out IContextualPacket? packet, context))
                        break;
                    int bytesConsumed = checked((int)(lengthBeforePacket - buffer.Length));
                    await packetHandler(packet, bytesConsumed);
                }

                long leftover = buffer.Length;
                pipeReader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted)
                {
                    leftoverBytes = leftover;
                    break;
                }
            }
            return leftoverBytes;
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private async Task HandleClientSendingAsync(CancellationToken token)
    {
        // TODO avoid using MemoryStream
        MemoryStream ms = new(512);
        PriorityPacketChannel channelReader = sendChannel;
        TimeSpan batchInterval = server.SendBatchInterval;
        int batchSize = server.SendBatchSize;
        TimeProvider timeProvider = TimeProvider.System;

        // TODO yes obviously client should handle batching too

        // wait for data
        while (await channelReader.WaitToReadAsync(token))
        {
            int packetsCount = 0;
            Task? window = null;
            while (true)
            {
                // then read them
                bool flush = false;
                while (channelReader.TryRead(out var packet))
                {
                    WritePacket(ms, packet, this);
                    packetsCount++;
                    if (!packet.CanBatch || ms.Position >= batchSize)
                    {
                        flush = true;
                        break;
                    }
                }

                if (flush) break;

                // not full, wait for more data
                // and also start a timer, we'll flush when the timer elapses or the batch size is reached
                window ??= Task.Delay(batchInterval, timeProvider, token);
                Task<bool> waitTask = channelReader.WaitToReadAsync(token).AsTask();
                if (await Task.WhenAny(waitTask, window) == window)
                {
                    // timer elapsed or cancelled, flush it
                    await window;
                    break;
                }
                else
                {
                    // if channel completed, flush the remaining data and exit
                    // else, continue the loop to read more data
                    bool channelCompleted = !await waitTask;
                    if (channelCompleted)
                        break;
                    else
                        continue;
                }
            }

            int size = checked((int)ms.Position);
            Debug.Assert(size > 0);

            var mem = ms.GetBuffer().AsMemory(0, size);
            await networkConnection.Stream.WriteAsync(mem, token);
            metricsService.RecordPacketTcpUpload(packetsCount, size);

            ms.Seek(0, SeekOrigin.Begin);
        }
        logger.LogDebug("Sending task of id {id} finished.", ID);
    }

    internal static bool TryParsePacket(
        ref ReadOnlySequence<byte> sequence,
        [NotNullWhen(true)] out IContextualPacket? packet,
        IPacketSerializationContext context
    )
    {
        const int HeadSize = sizeof(ushort) * 2;
        if (sequence.Length < HeadSize)
        {
            packet = null;
            return false;
        }
        Span<byte> headSpan = stackalloc byte[HeadSize];
        sequence.Slice(0, HeadSize).CopyTo(headSpan);
        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(headSpan);
        ushort id = BinaryPrimitives.ReadUInt16LittleEndian(headSpan.Slice(sizeof(ushort)));

        ReadOnlySequence<byte> payloadSequence = sequence.Slice(HeadSize);
        if (payloadSequence.Length < size)
        {
            packet = null;
            return false;
        }

        byte[]? rented = null;
        Span<byte> payloadSpan = size <= 1024
            ? stackalloc byte[size]
            : (rented = ArrayPool<byte>.Shared.Rent(size)).AsSpan(0, size);
        try
        {
            payloadSequence.Slice(0, size).CopyTo(payloadSpan);
            sequence = payloadSequence.Slice(size);

            RefBinaryReader reader = new(payloadSpan);
            var readHandler = PacketRegistry.GetPacketReader(id);
            packet = readHandler(ref reader, context);
            return true;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void WritePacket(
        MemoryStream stream,
        IContextualPacket packet,
        IPacketSerializationContext context
    )
        => PacketFraming.WritePacket(stream, packet, context);
}
