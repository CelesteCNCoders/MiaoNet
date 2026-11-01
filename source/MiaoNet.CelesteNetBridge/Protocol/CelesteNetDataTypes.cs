using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace MiaoNet.CelesteNetBridge.Protocol;

/// <summary>
/// Encoders for the CelesteNet DataTypes the bridge needs to put on the wire.
/// </summary>
/// <remarks>
/// Byte layout follows <c>data_type_payload</c> in <c>source/Spike/cnet-min-server/server.py</c>
/// (itself a port of <c>CelesteNet.Shared/DataContext.cs:388-431</c>):
/// <code>
/// u16 flags | netstring typeID | netstring source
///     | u8 (Small) or u16 LE length (counts everything after it)
///     | u8 metaCount | metaCount * [netstring metaID ; u8 length ; bytes] | data
/// </code>
/// </remarks>
public static class CelesteNetDataTypes
{
    /// <summary>DataFlags.Unreliable, <c>CelesteNet.Shared/DataTypes/Data.cs:188-209</c>.</summary>
    public const ushort FlagUnreliable = 0x0001;

    /// <summary>DataFlags.CoreType.</summary>
    public const ushort FlagCoreType = 0x0010;

    /// <summary>DataFlags.NoStandardMeta.</summary>
    public const ushort FlagNoStandardMeta = 0x0020;

    /// <summary>DataFlags.Small: the length field is 1 byte instead of 2.</summary>
    public const ushort FlagSmall = 0x2000;

    private const string Source = "CelesteNet.Shared";

    /// <summary>
    /// Encodes one DataType payload (without the outer TCP frame length).
    /// </summary>
    /// <param name="flags">DataFlags to write verbatim.</param>
    /// <param name="typeId">DataType ID, e.g. <c>"playerInfo"</c>.</param>
    /// <param name="data">Type-specific field bytes.</param>
    /// <param name="metas">Optional metas, each a netstring ID plus a u8-length-prefixed body.</param>
    public static byte[] Encode(
        ushort flags,
        string typeId,
        ReadOnlySpan<byte> data,
        IReadOnlyList<(string MetaId, byte[] Body)>? metas = null
    )
    {
        ArgumentNullException.ThrowIfNull(typeId);

        int metaCount = metas?.Count ?? 0;
        if (metaCount > byte.MaxValue)
        {
            throw new ArgumentException(
                $"A DataType carries at most {byte.MaxValue} metas, got {metaCount}.",
                nameof(metas)
            );
        }

        ArrayBufferWriter<byte> body = new();
        WriteByte(body, (byte)metaCount);
        if (metas is not null)
        {
            foreach ((string metaId, byte[] metaBody) in metas)
            {
                ArgumentNullException.ThrowIfNull(metaId);
                ArgumentNullException.ThrowIfNull(metaBody);
                if (metaBody.Length > byte.MaxValue)
                {
                    throw new ArgumentException(
                        $"Meta '{metaId}' body is {metaBody.Length} B, at most {byte.MaxValue} B.",
                        nameof(metas)
                    );
                }

                WriteNetString(body, metaId);
                WriteByte(body, (byte)metaBody.Length);
                body.Write(metaBody);
            }
        }
        body.Write(data);

        ArrayBufferWriter<byte> payload = new(64 + body.WrittenCount);
        WriteUInt16(payload, flags);
        WriteNetString(payload, typeId);
        WriteNetString(payload, Source);
        if ((flags & FlagSmall) != 0)
        {
            if (body.WrittenCount > byte.MaxValue)
            {
                throw new ArgumentException(
                    $"Small payload body is {body.WrittenCount} B, at most {byte.MaxValue} B.",
                    nameof(data)
                );
            }

            WriteByte(payload, (byte)body.WrittenCount);
        }
        else
        {
            if (body.WrittenCount > ushort.MaxValue)
            {
                throw new ArgumentException(
                    $"Payload body is {body.WrittenCount} B, at most {ushort.MaxValue} B.",
                    nameof(data)
                );
            }

            WriteUInt16(payload, (ushort)body.WrittenCount);
        }
        payload.Write(body.WrittenSpan);
        return payload.WrittenSpan.ToArray();
    }

    /// <summary>DataLowLevelKeepAlive: no fields, sent as a heartbeat.</summary>
    public static byte[] KeepAlive()
    {
        return Encode(FlagSmall | FlagCoreType, "keepAlive", default);
    }

    /// <summary>DataTickRate: one float, the server tick rate.</summary>
    public static byte[] TickRate(float rate)
    {
        Span<byte> data = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(data, rate);
        return Encode(FlagSmall, "tickRate", data);
    }

    /// <summary>
    /// DataPlayerInfo: the player ID lives in a <c>MetaRef</c>, the payload is three netstrings.
    /// </summary>
    /// <param name="alive">
    /// False sends a tombstone. The client then frees the reference instead of storing it
    /// (<c>DataContext.SetRef</c>), which is how a player is removed from the world.
    /// </param>
    public static byte[] PlayerInfo(uint id, string name, bool alive = true)
    {
        ArgumentNullException.ThrowIfNull(name);

        ArrayBufferWriter<byte> data = new();
        WriteNetString(data, name);
        WriteNetString(data, name);
        WriteNetString(data, name);

        IReadOnlyList<(string MetaId, byte[] Body)> metas = new[] { ("ref", EncodeRefMeta(id, alive)) };
        return Encode(0x0000, "playerInfo", data.WrittenSpan, metas);
    }

    /// <summary>DataChannelList: channel count, then name/id/player-count/player IDs per channel.</summary>
    public static byte[] ChannelList(IReadOnlyList<(string Name, uint Id, uint[] Players)> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);

        ArrayBufferWriter<byte> data = new();
        WriteUInt32(data, (uint)channels.Count);
        foreach ((string name, uint id, uint[] players) in channels)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(players);

            WriteNetString(data, name);
            WriteUInt32(data, id);
            WriteUInt32(data, (uint)players.Length);
            foreach (uint playerId in players)
                WriteUInt32(data, playerId);
        }

        return Encode(0x0000, "channelList", data.WrittenSpan);
    }

    /// <summary>DataReady: no fields. This is the only switch that sets the client's IsReady.</summary>
    public static byte[] Ready()
    {
        return Encode(FlagSmall, "ready", default);
    }

    private static byte[] EncodeRefMeta(uint id, bool alive)
    {
        // MetaRef: 7-bit encoded player ID followed by the IsAlive flag.
        byte[] body = new byte[6];
        int length = Write7BitEncodedInt(body, id);
        body[length++] = alive ? (byte)1 : (byte)0;
        return body[..length];
    }

    private static int Write7BitEncodedInt(Span<byte> destination, uint value)
    {
        int written = 0;
        while (value >= 0x80)
        {
            destination[written++] = (byte)(value | 0x80);
            value >>= 7;
        }
        destination[written++] = (byte)value;
        return written;
    }

    private static void WriteNetString(ArrayBufferWriter<byte> writer, string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> destination = writer.GetSpan(byteCount + 1);
        Encoding.UTF8.GetBytes(value, destination);
        destination[byteCount] = 0;
        writer.Advance(byteCount + 1);
    }

    private static void WriteByte(ArrayBufferWriter<byte> writer, byte value)
    {
        writer.GetSpan(1)[0] = value;
        writer.Advance(1);
    }

    private static void WriteUInt16(ArrayBufferWriter<byte> writer, ushort value)
    {
        Span<byte> destination = writer.GetSpan(sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
        writer.Advance(sizeof(ushort));
    }

    private static void WriteUInt32(ArrayBufferWriter<byte> writer, uint value)
    {
        Span<byte> destination = writer.GetSpan(sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        writer.Advance(sizeof(uint));
    }
}
