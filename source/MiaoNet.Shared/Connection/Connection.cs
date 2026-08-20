using System.Security.Authentication;

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

    public const int PacketHeaderSize = sizeof(ushort) * 2;

    public const int MaxPayloadSize = ushort.MaxValue;

    // allows only TLS 1.2 or TLS 1.3
    public const SslProtocols AllowedSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

    public static readonly Version Version = new(0, 5, 0);

    /// <summary>
    /// Returns whether two MiaoNet versions are compatible according to the
    /// protocol's SemVer policy. Patch releases are wire-compatible, while a
    /// change to major requires a matching client and server.
    /// </summary>
    public static bool IsVersionCompatible(Version client, Version server)
        => client.Major == server.Major && client.Minor <= server.Minor && (client.Major != 0 || client.Minor == server.Minor);
}
