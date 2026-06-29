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
    public const int UdpBufferSize = 1344;
    public const int MaxPacketPartSize = 4096;
    public const int PacketChannelSize = 64;
    public const int PacketBatchSize = 1344;

    // TODO timeout of request
    public delegate Task ResponseHandler(PacketResponse response);
    public delegate Task ResponseHandler<in TResponse>(TResponse response) where TResponse : PacketResponse;

    private int currentRequestID;
    private readonly ConcurrentDictionary<int, ResponseHandler> pendingRequests;

    private readonly ILogger<MiaoClientConnection> logger;
    private readonly MiaoServerService server;
    private readonly MiaoMetricsService metricsService;

    private readonly INetworkConnection networkConnection;
    private readonly CancellationTokenSource cts;
    private readonly Pipe pipe;

    public int ID { get; }

    public ServerPlayer Player { get; }

    public PooledStringManager PooledStringManager { get; }

    private readonly Channel<IContextualPacket> sendChannel;

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
        serverPlayer.Connection = this;

        cts = new CancellationTokenSource();
        pipe = new();
        pendingRequests = new();

        UnboundedChannelOptions options = new() { SingleReader = true };
        sendChannel = Channel.CreateUnbounded<IContextualPacket>(options);
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
            Task completed = await Task.WhenAny(receivingTask, processingTask, sendingTask);
            await cts.CancelAsync();
            //sendChannel.Writer.Complete();
            await completed;
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
        => sendChannel.Writer.WriteAsync(packet);

    public bool TryQueuePacket(IContextualPacket packet)
        => sendChannel.Writer.TryWrite(packet);

    // TODO maybe we can add a UserParam parameter to avoid closure
    // TODO timeout
    // TODO cancelling
    public ValueTask RequestAsync<TResponse>(PacketRequest<TResponse> packet, ResponseHandler<TResponse> callback)
        where TResponse : PacketResponse
    {
        int id = Interlocked.Increment(ref currentRequestID);
        packet.RequestID = id;
        bool success = pendingRequests.TryAdd(id, (packet) => callback((TResponse)packet));
        Debug.Assert(success);
        return QueuePacketAsync(packet);
    }

    public ValueTask ResponseAsync<TResponse>(PacketRequest<TResponse> request, TResponse response)
        where TResponse : PacketResponse
    {
        response.RequestID = request.RequestID;
        return QueuePacketAsync(response);
    }

    public ResponseHandler? OnResponse(PacketResponse response)
    {
        if (pendingRequests.TryRemove(response.RequestID, out var handler))
            return handler;

        logger.LogWarning(
            "Could not find source request id of response {id}, type is {type}.",
            response.RequestID,
            response.GetType().FullName
        );
        foreach (var item in pendingRequests)
            logger.LogWarning("pendingRequests has key: {key}", item.Key);

        return null;
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
        var pipeReader = pipe.Reader;
        while (true)
        {
            var result = await pipeReader.ReadAsync(token);
            if (result.IsCompleted)
                break;

            var oldBuffer = result.Buffer;
            var buffer = result.Buffer;
            while (TryParsePacket(ref buffer, out IContextualPacket? packet, this))
            {
                metricsService.RecordPacketTcpDownload((int)(oldBuffer.Length - buffer.Length));
                await server.HandlePacketAsync(this, packet);
            }

            pipeReader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted)
            {
                await pipeReader.CompleteAsync();
                break;
            }
        }
        await pipeReader.CompleteAsync();
        logger.LogDebug("Processing task of id {id} finished.", ID);
    }

    private async Task HandleClientSendingAsync(CancellationToken token)
    {
        // TODO avoid using MemoryStream
        MemoryStream ms = new(512);
        var channelReader = sendChannel.Reader;
        while (await channelReader.WaitToReadAsync(token))
        {
            while (channelReader.TryRead(out var packet) && ms.Position < PacketBatchSize)
                WritePacket(ms, packet, this);

            int size = checked((int)ms.Position);
            var buffer = ms.GetBuffer().AsMemory(0, size);

            await networkConnection.Stream.WriteAsync(buffer, token);
            metricsService.RecordPacketTcpUpload(size);

            ms.Seek(0, SeekOrigin.Begin);
        }
        logger.LogDebug("Sending task of id {id} finished.", ID);
    }

    private static bool TryParsePacket(
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

        Span<byte> payloadSpan = stackalloc byte[size];

        payloadSequence.Slice(0, size).CopyTo(payloadSpan);
        sequence = payloadSequence.Slice(size);

        RefBinaryReader reader = new(payloadSpan);
        var readHandler = PacketRegistry.GetPacketReader(id);
        packet = readHandler(ref reader, context);
        return true;
    }

    private static void WritePacket(
        MemoryStream stream,
        IContextualPacket packet,
        IPacketSerializationContext context
    )
    {
        stream.Seek(sizeof(ushort) + sizeof(ushort), SeekOrigin.Current);
        long start = stream.Position;
        RefBinaryWriter w = new(stream);
        packet.Serialize(ref w, context);
        ushort size = checked((ushort)(stream.Position - start));
        ushort id = PacketRegistry.GetPacketID(packet);
        stream.Seek(-size - sizeof(ushort) - sizeof(ushort), SeekOrigin.Current);
        w.Write(size);
        w.Write(id);
        stream.Seek(size, SeekOrigin.Current);
    }
}