using System.Diagnostics;

namespace MiaoNet.Shared;

public static class Connection
{
    public const int HandshakeHeadLength = 16;

    public static ReadOnlySpan<byte> HandshakeHead => [
        6, 3, 0, 1, 4,
        (byte)'M', (byte)'i', (byte)'a', (byte)'o',
        (byte)'N', (byte)'e', (byte)'t', (byte)'+',
        2, 0, 2
    ];

#if DEBUG
    static Connection()
    {
        Debug.Assert(HandshakeHead.Length == HandshakeHeadLength);
    }
#endif
}