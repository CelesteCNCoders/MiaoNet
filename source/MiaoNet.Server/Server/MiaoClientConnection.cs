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
    public const int MaxPendingRequests = 64;

    public delegate Task ResponseHandler(IPacketResponse response);
    public delegate Task ResponseHandler<in TResponse>(TResponse response) where TResponse : IPacketResponse;

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

    private readonly Channel<EnvelopedPacket> sendChannel;

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

        UnboundedChannelOptions options = new() { SingleReader = true };
        sendChannel = Channel.CreateUnbounded<EnvelopedPacket>(options);
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
            logger.LogInformation(AppEvents.Connection, "Connection aborted for {player}.", Player);
        }
        catch (OperationCanceledException)
        {
            networkConnection.Shutdown();
            logger.LogDebug(AppEvents.Connection, "Connection handling cancelled for {player}.", Player);
        }
        catch (Exception e)
        {
            logger.LogError(AppEvents.Connection, e, "Connection handling failed for {player}.", Player);
        }
        finally
        {
            await CancelPendingRequestsAsync();
            networkConnection.Dispose();
            logger.LogInformation(AppEvents.Connection, "Connection closed for {player}.", Player);
        }
    }

    public async Task DisconnectAsync(DisconnectReason reason, string? message = null)
    {
        cts.CancelAfter(server.DisconnectTimeout);
        await QueuePacketAsync(new PacketDisconnected(reason, message));
    }

    #region Packet

    public ValueTask QueuePacketAsync(IContextualPacket packet)
        => QueuePacketAsync(default, packet);

    public ValueTask QueuePacketAsync(PacketEnvelope envelope, IContextualPacket packet)
        => sendChannel.Writer.WriteAsync(new(envelope, packet));

    public bool TryQueuePacket(IContextualPacket packet)
        => TryQueuePacket(default, packet);

    public bool TryQueuePacket(PacketEnvelope envelope, IContextualPacket packet)
        => sendChannel.Writer.TryWrite(new(envelope, packet));

    // TODO maybe we can add a UserParam parameter to avoid closure
    public async ValueTask<bool> RequestAsync<TResponse>(
        IPacketRequest<TResponse> packet,
        ResponseHandler<TResponse> callback,
        TimeSpan timeout,
        Func<Task>? timeoutHandler = null,
        CancellationToken cancellationToken = default
    )
        where TResponse : IPacketResponse
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        if (Interlocked.Increment(ref pendingRequestCount) > MaxPendingRequests)
        {
            Interlocked.Decrement(ref pendingRequestCount);
            return false;
        }

        int id = Interlocked.Increment(ref currentRequestID);
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
        await QueuePacketAsync(PacketEnvelope.FromRequest(id), packet);
        return true;
    }

    public ValueTask ResponseAsync<TResponse>(PacketEnvelope requestEnvelope, TResponse response)
        where TResponse : IPacketResponse
        => QueuePacketAsync(PacketEnvelope.ReplyTo(requestEnvelope.RequestID), response);

    public ResponseHandler? TakeResponseHandler(int requestID)
    {
        if (TryTakePendingRequest(requestID, out var pending))
        {
            pending.CancellationTokenSource.Cancel();
            ResponseHandler handler = pending.Handler;
            pending.Dispose();
            return handler;
        }

        logger.LogWarning(
            AppEvents.Connection,
            "Response {id} from {player} has no matching pending request.",
            requestID,
            Player
        );

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
            logger.LogError(AppEvents.Connection, e, "Timeout handler of request {id} failed for {player}.", id, Player);
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
                            "Cancellation handler of request {id} failed for {player}.",
                            id,
                            Player
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
        logger.LogDebug("Receiving task finished for {player}.", Player);
    }

    private async Task HandleClientProcessingAsync(CancellationToken token)
    {
        long leftoverBytes = await ProcessPacketsAsync(
            pipe.Reader,
            this,
            async (frame, bytesConsumed) =>
            {
                metricsService.RecordPacketTcpDownload(1, bytesConsumed);
                await server.HandlePacketAsync(this, frame.Envelope, frame.Packet);
            },
            token
        );
        if (leftoverBytes > 0)
        {
            logger.LogWarning(
                AppEvents.Connection,
                "Connection closed for {player} with {leftover} bytes that do not form a complete packet frame.",
                Player,
                leftoverBytes
            );
        }
        logger.LogDebug("Processing task finished for {player}.", Player);
    }

    internal static async Task<long> ProcessPacketsAsync(
        PipeReader pipeReader,
        IPacketSerializationContext context,
        Func<EnvelopedPacket, int, ValueTask> packetHandler,
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
                    if (!TryParsePacket(ref buffer, out EnvelopedPacket frame, context))
                        break;
                    int bytesConsumed = checked((int)(lengthBeforePacket - buffer.Length));
                    await packetHandler(frame, bytesConsumed);
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
        ByteArrayBufferWriter batch = new(512);
        var channelReader = sendChannel.Reader;
        TimeSpan batchInterval = server.SendBatchInterval;
        int batchSize = server.SendBatchSize;
        TimeProvider timeProvider = TimeProvider.System;

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
                    PacketFraming.WritePacket(batch, packet.Envelope, packet.Packet, this);
                    packetsCount++;
                    if (!packet.Packet.CanBatch || batch.WrittenCount >= batchSize)
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

            int size = batch.WrittenCount;
            Debug.Assert(size > 0);

            await networkConnection.Stream.WriteAsync(batch.WrittenMemory, token);
            metricsService.RecordPacketTcpUpload(packetsCount, size);

            batch.Clear();
        }
        logger.LogDebug("Sending task finished for {player}.", Player);
    }

    internal static bool TryParsePacket(
        ref ReadOnlySequence<byte> sequence,
        out EnvelopedPacket frame,
        IPacketSerializationContext context
    )
    {
        const int HeadSize = Connection.PacketHeaderSize;
        if (sequence.Length < HeadSize)
        {
            frame = default;
            return false;
        }
        Span<byte> headSpan = stackalloc byte[HeadSize];
        sequence.Slice(0, HeadSize).CopyTo(headSpan);
        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(headSpan);
        byte id = headSpan[sizeof(ushort)];
        byte flags = headSpan[sizeof(ushort) + sizeof(byte)];

        ReadOnlySequence<byte> payloadSequence = sequence.Slice(HeadSize);
        if (payloadSequence.Length < size)
        {
            frame = default;
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
            PacketEnvelope envelope = PacketEnvelope.ReadOptional(ref reader, (PacketEnvelopeFlags)flags);
            var readHandler = PacketRegistry.GetPacketReader(id);
            frame = new(envelope, readHandler(ref reader, context));
            return true;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
