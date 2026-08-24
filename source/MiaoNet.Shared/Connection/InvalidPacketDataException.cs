namespace MiaoNet.Shared;

internal sealed class InvalidPacketDataException : Exception
{
    public byte[] Payload { get; }

    public InvalidPacketDataException(byte[] payload, Exception? innerException)
        : base(null, innerException)
    {
        Payload = payload;
    }
}
