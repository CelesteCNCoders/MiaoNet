using System.Buffers.Binary;
using System.Text;

namespace MiaoNet.CelesteNetBridge.Protocol;

/// <summary>
/// Best-effort reader for the type ID of an inbound DataType payload, used for logging.
/// </summary>
/// <remarks>
/// Mirrors <c>read_type_id</c> in <c>source/Spike/cnet-min-server/server.py</c>. This never
/// deserializes the type's fields: the payload is left untouched and only the
/// <c>u16 flags | netstring typeID</c> prefix is inspected.
/// </remarks>
public static class CelesteNetTypeReader
{
    /// <summary>DataFlags slim marker: the type ID is a mapping varint, not a string.</summary>
    private const ushort FlagSlim = 0x8000;

    /// <summary>
    /// Reads the type ID from a DataType payload.
    /// </summary>
    /// <returns>
    /// The decoded type ID, or <c>null</c> when the payload cannot be parsed (too short, missing
    /// terminator, empty type ID, or the slim mapping form which carries no string).
    /// </returns>
    public static string? TryReadTypeId(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 3)
            return null;

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        if ((flags & FlagSlim) != 0)
            return null;

        ReadOnlySpan<byte> typeIdArea = payload[2..];
        int terminator = typeIdArea.IndexOf((byte)0);
        if (terminator <= 0)
            return null;

        return Encoding.UTF8.GetString(typeIdArea[..terminator]);
    }
}
