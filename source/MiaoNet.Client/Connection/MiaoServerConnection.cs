using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using System.Threading.Tasks.Sources;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class MiaoServerConnection : IDisposable
{
    private static readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;

    private readonly Socket socket;
    private readonly SslStream sslStream;

    // TODO use memory stream is not so good
    private readonly MemoryStream sendMemoryStream;

    private readonly ConcurrentQueue<IContextualPacket> sendQueue;
    private readonly SemaphoreSlim sendSemaphore;

    private MiaoServerConnection(Socket socket, SslStream sslStream)
    {
        this.socket = socket;
        this.sslStream = sslStream;

        sendMemoryStream = new(512);
        sendMemoryStream.Seek(2, SeekOrigin.Begin);

        sendQueue = new();
        sendSemaphore = new(0);
    }

    public static async Task<MiaoServerConnection> CreateAsync(
        EndPoint endPoint,
        string hostName,
        HandshakeData handshakeData,
        CancellationToken token
    )
    {
        Socket socket = new(SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;

        await socket.ConnectAsync(endPoint, token);
#if !USE_LOCALHOST_PFX
        var sslStream = new SslStream(new NetworkStream(socket));
#else
        var certStream = typeof(MiaoServerConnection).Assembly.GetManifestResourceStream("localhost.pfx")!;
        byte[] certRawData = new byte[certStream.Length];
        certStream.ReadExactly(certRawData, 0, certRawData.Length);
        var cert = new X509Certificate2(certRawData);
        var sslStream = new SslStream(new NetworkStream(socket), false, (sender, certificate, chain, errors) =>
        {
            if (certificate == null) return false;
            var remote = new X509Certificate2(certificate);
            return string.Equals(remote.Thumbprint, cert.Thumbprint, StringComparison.OrdinalIgnoreCase);
        });
#endif
        bool checkRevocation = !MiaoNetModule.Settings.IgnoreCertRevocationStatus;
        SslClientAuthenticationOptions options = new()
        {
            TargetHost = hostName,
            EnabledSslProtocols = Connection.AllowedSslProtocols,
            CertificateRevocationCheckMode = checkRevocation
                ? X509RevocationMode.Online
                : X509RevocationMode.NoCheck
        };

        await sslStream.AuthenticateAsClientAsync(options, token);
        await sslStream.WriteAsync(Connection.HandshakeHead, token);

        return new(socket, sslStream);
    }

    public async Task<Version?> MakeVersionCheck(Version clientVersion, CancellationToken token)
    {
        ushort major = (ushort)clientVersion.Major;
        ushort minor = (ushort)clientVersion.Minor;
        ushort build = (ushort)clientVersion.Build;

        const int VersionLength = 3 * sizeof(ushort);
        var buffer = pool.Rent(VersionLength);
        try
        {
            var memory = buffer.AsMemory(0, VersionLength);
            var span = memory.Span;
            BinaryPrimitives.WriteUInt16LittleEndian(span[0..2], major);
            BinaryPrimitives.WriteUInt16LittleEndian(span[2..4], minor);
            BinaryPrimitives.WriteUInt16LittleEndian(span[4..6], build);
            await sslStream.WriteAsync(memory, token);
        }
        finally
        {
            pool.Return(buffer);
        }

        buffer = pool.Rent(VersionLength + 1);
        try
        {
            var memory = buffer.AsMemory(0, VersionLength + 1);
            await sslStream.ReadExactlyAsync(memory[0..1], token);
            bool passed = memory.Span[0] != 0;
            if (!passed)
            {
                await sslStream.ReadExactlyAsync(memory[1..(VersionLength + 1)], token);
                var span = memory.Span;
                ushort majorServer = BinaryPrimitives.ReadUInt16LittleEndian(span[1..3]);
                ushort minorServer = BinaryPrimitives.ReadUInt16LittleEndian(span[3..5]);
                ushort buildServer = BinaryPrimitives.ReadUInt16LittleEndian(span[5..7]);
                return new(majorServer, minorServer, buildServer);
            }
            else
            {
                return null;
            }
        }
        finally
        {
            pool.Return(buffer);
        }
    }

    public async Task<HandshakeAckData> MakeHandshakeAsync(HandshakeData handshakeData, CancellationToken token)
    {
        {
            MemoryStream ms = new MemoryStream(64);
            ms.Seek(2, SeekOrigin.Begin);
            RefBinaryWriter writer = new(ms);
            writer.Write(handshakeData);
            ushort size = (ushort)(ms.Position - sizeof(ushort));
            ms.Seek(0, SeekOrigin.Begin);
            writer.Write(size);
            var memory = ms.GetBuffer().AsMemory(0, size + sizeof(ushort));
            await sslStream.WriteAsync(memory, token);
        }

        {
            ushort size;
            var buffer = pool.Rent(sizeof(ushort));
            try
            {
                var memory = buffer.AsMemory(0, sizeof(ushort));
                await sslStream.ReadExactlyAsync(memory, token);
                size = BinaryPrimitives.ReadUInt16LittleEndian(memory.Span);
            }
            finally
            {
                pool.Return(buffer);
            }

            buffer = pool.Rent(size);
            try
            {
                var memory = buffer.AsMemory(0, size);
                await sslStream.ReadExactlyAsync(memory, token);

                RefBinaryReader reader = new(memory.Span);
                var ack = reader.Read<HandshakeAckData>();
                return ack;
            }
            finally
            {
                pool.Return(buffer);
            }
        }
    }

    public void Dispose()
    {
        sendSemaphore.Dispose();
        sslStream.Dispose();
        if (socket.Connected)
            socket.Shutdown(SocketShutdown.Both);
        socket.Dispose();
    }

    public async Task SendPacketsLoopAsync(IPacketSerializationContext context, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            while (sendQueue.TryDequeue(out IContextualPacket? packet))
                await SendPacketAsync(packet, context, token);
            if (token.IsCancellationRequested)
                return;
            await sendSemaphore.WaitAsync(token);
        }
    }

    public int QueuePacket(IContextualPacket packet)
    {
        sendQueue.Enqueue(packet);
        int count = sendQueue.Count;
        sendSemaphore.Release();
        return count;
    }

    public async Task SendPacketAsync(IContextualPacket packet, IPacketSerializationContext context, CancellationToken token)
    {
        sendMemoryStream.Seek(2, SeekOrigin.Begin);
        RefBinaryWriter writer = new(sendMemoryStream);
        ushort type = PacketRegistry.GetPacketID(packet);
        writer.Write(type);
        packet.Serialize(ref writer, context);
        ushort length = (ushort)(sendMemoryStream.Position - 2 * sizeof(ushort));
        sendMemoryStream.Seek(0, SeekOrigin.Begin);
        writer.Write(length);
        await sslStream.WriteAsync(sendMemoryStream.GetBuffer().AsMemory(0, length + 2 * sizeof(ushort)), token);
    }

    public async Task<IContextualPacket?> ReceivePacketAsync(IPacketSerializationContext context, CancellationToken token)
    {
        const int HeadSize = 2 * sizeof(ushort);

        var headBuffer = pool.Rent(HeadSize);
        ushort size, type;
        try
        {
            Memory<byte> headMemory = headBuffer.AsMemory().Slice(0, HeadSize);
            int count = await sslStream.ReadAtLeastAsync(headMemory, HeadSize, false, token);
            if (count < HeadSize)
                return null;

            size = BinaryPrimitives.ReadUInt16LittleEndian(headMemory.Span);
            type = BinaryPrimitives.ReadUInt16LittleEndian(headMemory.Span.Slice(sizeof(ushort)));
        }
        finally
        {
            pool.Return(headBuffer);
        }

        var payloadBuffer = pool.Rent(size);
        try
        {
            Memory<byte> payloadMemory = payloadBuffer.AsMemory().Slice(0, size);
            int count = await sslStream.ReadAtLeastAsync(payloadMemory, size, false, token);
            if (count < size)
                return null;

            try
            {
                RefBinaryReader reader = new(payloadMemory.Span);
                var readHandler = PacketRegistry.GetPacketReader(type);
                IContextualPacket packet = readHandler(ref reader, context);
                return packet;
            }
            catch (Exception e)
            {
                Logger.Error(
                    LT.MiaoNetPacketReading,
                    $"Read packet failed, size: {size}, type: {type}. Raw payload:\n" +
                        Convert.ToBase64String(payloadMemory.ToArray())
                );
                Logger.LogDetailed(e, LT.MiaoNetPacketReading);
                throw;
            }
        }
        finally
        {
            pool.Return(payloadBuffer);
        }
    }
}