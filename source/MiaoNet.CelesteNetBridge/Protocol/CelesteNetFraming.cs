using System.Buffers.Binary;
using System.IO;

namespace MiaoNet.CelesteNetBridge.Protocol;

/// <summary>
/// TCP framing for the CelesteNet wire protocol: a u16 little-endian payload length followed by
/// the DataType payload, with no padding, fragmentation or checksum.
/// </summary>
/// <remarks>
/// Mirrors <c>tcp_frame</c> in <c>source/Spike/cnet-min-server/server.py</c> and
/// <c>CelesteNet.Client/CelesteNetClientTCPUDPConnection.cs:161-165</c>.
/// </remarks>
public static class CelesteNetFraming
{
    /// <summary>Payload size limit advertised in the teapot handshake.</summary>
    public const int MaxPacketSize = 2048;

    /// <summary>Wraps one DataType payload into a length-prefixed TCP frame.</summary>
    public static byte[] Wrap(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPacketSize)
        {
            throw new ArgumentException(
                $"Payload is {payload.Length} B, over MaxPacketSize ({MaxPacketSize} B).",
                nameof(payload)
            );
        }

        byte[] frame = new byte[sizeof(ushort) + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)payload.Length);
        payload.CopyTo(frame.AsSpan(sizeof(ushort)));
        return frame;
    }

    /// <summary>
    /// Tries to cut one complete frame out of <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">Bytes received so far; may hold a partial frame or several frames.</param>
    /// <param name="payload">The payload of the first complete frame (empty when returning false).</param>
    /// <param name="consumed">
    /// Bytes eaten by this frame, including its 2-byte length prefix (0 when returning false).
    /// </param>
    /// <returns>
    /// <c>false</c> when more bytes are needed. A frame that announces more than
    /// <see cref="MaxPacketSize"/> is a protocol violation and throws <see cref="InvalidDataException"/>.
    /// </returns>
    public static bool TryRead(ReadOnlySpan<byte> buffer, out byte[] payload, out int consumed)
    {
        payload = Array.Empty<byte>();
        consumed = 0;

        if (buffer.Length < sizeof(ushort))
            return false;

        int length = BinaryPrimitives.ReadUInt16LittleEndian(buffer);
        if (length > MaxPacketSize)
        {
            throw new InvalidDataException(
                $"Peer sent packet over maximum size ({length} B > {MaxPacketSize} B)."
            );
        }

        if (buffer.Length < sizeof(ushort) + length)
            return false;

        payload = buffer.Slice(sizeof(ushort), length).ToArray();
        consumed = sizeof(ushort) + length;
        return true;
    }
}
