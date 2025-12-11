namespace MiaoNet.Shared;

public static class Connection
{
    public static readonly ReadOnlyMemory<byte> HandshakeHead = new byte[HandshakeHeadLength] {
        6, 3, 0, 1, 4,
        (byte)'M', (byte)'i', (byte)'a', (byte)'o',
        (byte)'N', (byte)'e', (byte)'t', (byte)'+',
        2, 0, 2
    };

    public const int HandshakeHeadLength = 16;
}