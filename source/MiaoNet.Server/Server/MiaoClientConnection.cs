using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Channels;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

[DebuggerDisplay("ID = {ID}, Player = {Player}")]
public sealed class MiaoClientConnection
{
    public const int TcpBufferSize = 2048;
    public const int UdpBufferSize = 1344;
    public const int MaxPacketPartSize = 4096;
    public const int PacketChannelSize = 64;

    private readonly ILogger<MiaoClientConnection> logger;
    private readonly MiaoServerService server;

    private readonly Socket socket;
    private readonly CancellationTokenSource cts;
    private readonly Pipe pipe;

    public int ID { get; private set; }
    public ServerPlayer Player { get; private set; }

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
        // TODO initial states
        Player = onlinePlayer;

        cts = new CancellationTokenSource();
        pipe = new();

        sendChannel = Channel.CreateUnbounded<SerializedPacket>(new UnboundedChannelOptions() { SingleReader = true });
    }

    public async Task HandleClientConnectAsync()
    {
        var token = cts.Token;
        Task receiveTask = HandleClientReceiveAsync(token);
        Task processTask = HandleClientProcessAsync(token);
        Task sendingTask = HandleClientSendingAsync(token);

        try
        {
            await Task.WhenAll(receiveTask, processTask, sendingTask);
        }
        finally
        {
            if (socket.Connected)
                socket.Shutdown(SocketShutdown.Both);
            socket.Close();
            logger.LogInformation(AppEvents.Connection, "Connection id {id} closed.", ID);
        }
    }

    public void Disconnect(KickedReason reason)
    {
        // TODO tell the client that they were kicked :(
        cts.Cancel();
    }

    public ValueTask SendPacketAsync(IPacket packet)
        => sendChannel.Writer.WriteAsync(new SerializedPacket(ArrayPool<byte>.Shared, packet, 1));

    public ValueTask SendPacketAsync(SerializedPacket packet)
        => sendChannel.Writer.WriteAsync(packet);

    public bool TrySendPacket(SerializedPacket packet)
        => sendChannel.Writer.TryWrite(packet);

    private async Task HandleClientReceiveAsync(CancellationToken token)
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

    private async Task HandleClientProcessAsync(CancellationToken token)
    {
        var pipeReader = pipe.Reader;
        try
        {
            while (true)
            {
                var result = await pipeReader.ReadAsync(token);
                var buffer = result.Buffer;
                while (TryParsePacket(ref buffer, out IPacket? packet))
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

    private bool TryParsePacket(ref ReadOnlySequence<byte> sequence, [NotNullWhen(true)] out IPacket? packet)
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
        packet = PacketRegistry.ReadPacket(typeID, ref reader);
        return true;
    }
}