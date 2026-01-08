using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
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

    // TODO timeout of request
    public delegate Task ResponseHandler(PacketResponse response);
    public delegate Task ResponseHandler<in TResponse>(TResponse response) where TResponse : PacketResponse;
    private int currentRequestID;
    private readonly ConcurrentDictionary<int, ResponseHandler> pendingRequests;

    private readonly ILogger<MiaoClientConnection> logger;
    private readonly MiaoServerService server;

    private readonly Socket socket;
    private readonly CancellationTokenSource cts;
    private readonly Pipe pipe;

    public int ID { get; private set; }

    public ServerPlayer Player { get; private set; }

    public PooledStringManager PooledStringManager { get; }

    private readonly Channel<SerializedPacket> sendChannel;

    public MiaoClientConnection(
        int id, Socket socket, ServerPlayer onlinePlayer,
        ILogger<MiaoClientConnection> logger,
        MiaoServerService server
    )
    {
        ID = id;
        this.logger = logger;
        this.server = server;
        this.socket = socket;
        Player = onlinePlayer;

        cts = new CancellationTokenSource();
        pipe = new();
        pendingRequests = new();

        UnboundedChannelOptions options = new() { SingleReader = true };
        sendChannel = Channel.CreateUnbounded<SerializedPacket>(options);
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
            await Task.WhenAll(receivingTask, processingTask, sendingTask);
        }
        finally
        {
            if (socket.Connected)
                socket.Shutdown(SocketShutdown.Both);
            socket.Close();
            logger.LogInformation(AppEvents.Connection, "Connection id {id} closed.", ID);
        }
    }
    
    public async Task DisconnectAsync(DisconnectReason reason, string? message = null)
    {
        cts.CancelAfter(server.DisconnectTimeout);
        await QueuePacketAsync(new PacketDisconnected(reason, message));
    }

    public ValueTask QueuePacketAsync(IContextualPacket packet)
        => sendChannel.Writer.WriteAsync(new SerializedPacket(ArrayPool<byte>.Shared, packet, this, 1));

    public ValueTask QueuePacketAsync(SerializedPacket packet)
        => sendChannel.Writer.WriteAsync(packet);

    public bool TryQueuePacket(SerializedPacket packet)
        => sendChannel.Writer.TryWrite(packet);

    // TODO maybe we can add a UserParam parameter to avoid closure
    // TODO timeout
    // TODO cancelling
    public ValueTask RequestAsync<TResponse>(PacketRequest<TResponse> packet, ResponseHandler<TResponse> callback)
        where TResponse : PacketResponse
    {
        int id = packet.RequestID = Interlocked.Increment(ref currentRequestID);
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
        => pendingRequests.TryRemove(response.RequestID, out var handler) ? handler : null;

    private async Task HandleClientReceivingAsync(CancellationToken token)
    {
        var pipeWriter = pipe.Writer;
        try
        {
            while (true)
            {
                var mem = pipeWriter.GetMemory(TcpBufferSize);
                int received = await socket.ReceiveAsync(mem, token);
                if (received is 0 || token.IsCancellationRequested)
                    break;

                pipeWriter.Advance(received);

                FlushResult flushResult = await pipeWriter.FlushAsync(token);
                if (flushResult.IsCompleted)
                    break;
            }
        }
        catch (SocketException e)
        when (e.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted)
        {
            logger.LogInformation(AppEvents.Connection, "Connection aborted, id {id}.", ID);
            await pipeWriter.CompleteAsync();
        }
        catch (OperationCanceledException)
        {
            logger.LogTrace(AppEvents.Connection, "Connection id {id} receiving cancelled.", ID);
            await pipeWriter.CompleteAsync();
        }
        catch (Exception e)
        {
            logger.LogError(AppEvents.Connection, e, "Exception when receiving from id {id}.", ID);
            await pipeWriter.CompleteAsync(e);
        }
        finally
        {
            cts.Cancel();
        }
    }

    private async Task HandleClientProcessingAsync(CancellationToken token)
    {
        var pipeReader = pipe.Reader;
        try
        {
            while (true)
            {
                var result = await pipeReader.ReadAsync(token);
                var buffer = result.Buffer;
                while (TryParsePacket(ref buffer, out IContextualPacket? packet, this))
                    await server.HandlePacketAsync(this, packet);

                pipeReader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted)
                {
                    await pipeReader.CompleteAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogTrace(AppEvents.Connection, "Connection id {id} processing cancelled.", ID);
            await pipeReader.CompleteAsync();
        }
        catch (Exception e)
        {
            logger.LogError(AppEvents.Connection, e, "Exception when processing id: {id}.", ID);
            await pipeReader.CompleteAsync(e);
        }
        finally
        {
            cts.Cancel();
        }
    }

    private async Task HandleClientSendingAsync(CancellationToken token)
    {
        var channelReader = sendChannel.Reader;
        try
        {
            await foreach (var packet in channelReader.ReadAllAsync(token))
            {
                await socket.SendAsync(packet.ArraySegment, token);
                packet.OnConsumed();
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogTrace(AppEvents.Connection, "Connection id {id} processing cancelled.", ID);
        }
        catch (SocketException e)
        when (e.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted)
        {
            logger.LogInformation(AppEvents.Connection, "Connection aborted, id {id}.", ID);
        }
        catch (Exception e)
        {
            logger.LogError(AppEvents.Connection, e, "Error occurred when process sending client id {id}.", ID);
        }
        finally
        {
            cts.Cancel();
        }
    }

    private bool TryParsePacket(
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
        ushort typeID = BinaryPrimitives.ReadUInt16LittleEndian(headSpan.Slice(sizeof(ushort)));

        ReadOnlySequence<byte> payloadSequence = sequence.Slice(HeadSize);
        if (payloadSequence.Length < size)
        {
            packet = null;
            return false;
        }

        // TODO stackalloc
        Span<byte> payloadSpan = stackalloc byte[size];

        payloadSequence.Slice(0, size).CopyTo(payloadSpan);
        sequence = payloadSequence.Slice(size);

        RefBinaryReader reader = new(payloadSpan);
        var readHandler = PacketRegistry.GetPacketReader(typeID);
        packet = readHandler(ref reader, context);
        return true;
    }
}