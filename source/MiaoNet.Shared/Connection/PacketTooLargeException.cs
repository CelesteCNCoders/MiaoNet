using System.Globalization;

namespace MiaoNet.Shared;

public sealed class PacketTooLargeException : Exception
{
    public Type PacketType { get; }

    public long PayloadSize { get; }

    public int MaxPayloadSize { get; }

    public PacketTooLargeException(Type packetType, long payloadSize, int maxPayloadSize)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            SR.PacketTooLarge,
            packetType.FullName,
            payloadSize,
            maxPayloadSize
        ))
    {
        PacketType = packetType;
        PayloadSize = payloadSize;
        MaxPayloadSize = maxPayloadSize;
    }
}
