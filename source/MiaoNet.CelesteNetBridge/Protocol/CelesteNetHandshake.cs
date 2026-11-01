using System.Text;

namespace MiaoNet.CelesteNetBridge.Protocol;

/// <summary>
/// The "teapot" HTTP-style handshake CelesteNet performs before switching to binary frames.
/// </summary>
/// <remarks>
/// Mirrors <c>HANDSHAKE_RESPONSE</c> and <c>read_until_blank_line</c> in
/// <c>source/Spike/cnet-min-server/server.py</c>; see
/// <c>docs/local/research/minimal-cnet-server.md</c> section 1.
/// </remarks>
public static class CelesteNetHandshake
{
    /// <summary>The status line the client expects as the first line of the response.</summary>
    public const string TeapotLine = "HTTP/4.2 418 I'm a teapot";

    /// <summary>Size of <see cref="BuildResponse"/> in bytes; pinned by the spike's self test.</summary>
    public const int ResponseSize = 598;

    private const string Body = "Who wants some tea!";

    private static readonly byte[] Response = Encoding.UTF8.GetBytes(
        TeapotLine + "\r\n"
        + "Connection: keep-alive\r\n"
        + "CelesteNet-TeapotVersion: 2\r\n"
        + "CelesteNet-ConnectionToken: DEADBEEF\r\n"
        + "CelesteNet-ConnectionFeatures: \r\n"
        + "CelesteNet-Settings-MaxPacketSize: 2048\r\n"
        + "CelesteNet-Settings-MaxQueueSize: 1024\r\n"
        + "CelesteNet-Settings-MergeWindow: 15\r\n"
        + "CelesteNet-Settings-MaxHeartbeatDelay: 20\r\n"
        + "CelesteNet-Settings-HeartbeatInterval: 250\r\n"
        + "CelesteNet-Settings-UDPAliveScoreMax: 70\r\n"
        + "CelesteNet-Settings-UDPDowngradeScoreMin: -2\r\n"
        + "CelesteNet-Settings-UDPDowngradeScoreMax: 2\r\n"
        + "CelesteNet-Settings-UDPDeathScoreMin: -2\r\n"
        + "CelesteNet-Settings-UDPDeathScoreMax: -1\r\n"
        + "\r\n"
        + Body + "\r\n"
        + "\r\n"
    );

    /// <summary>
    /// Looks for the end of the client's HTTP-style request, i.e. the CRLFCRLF-terminated header
    /// block. A bare LFLF is accepted too, like the Python spike does.
    /// </summary>
    /// <param name="buffer">Bytes received so far.</param>
    /// <param name="firstLine">The request line without its line ending (empty when returning false).</param>
    /// <param name="requestBytes">Bytes taken by the whole request, terminator included.</param>
    /// <returns><c>false</c> when the blank line has not arrived yet.</returns>
    public static bool TryReadRequest(ReadOnlySpan<byte> buffer, out string firstLine, out int requestBytes)
    {
        firstLine = string.Empty;
        requestBytes = 0;

        int headerEnd = buffer.IndexOf("\r\n\r\n"u8);
        int terminatorLength = 4;
        int bareLineFeedEnd = buffer.IndexOf("\n\n"u8);
        if (bareLineFeedEnd >= 0 && (headerEnd < 0 || bareLineFeedEnd < headerEnd))
        {
            headerEnd = bareLineFeedEnd;
            terminatorLength = 2;
        }

        if (headerEnd < 0)
            return false;

        ReadOnlySpan<byte> head = buffer[..headerEnd];
        int lineEnd = head.IndexOf("\r\n"u8);
        if (lineEnd < 0)
            lineEnd = head.IndexOf((byte)'\n');
        if (lineEnd < 0)
            lineEnd = head.Length;

        firstLine = Encoding.UTF8.GetString(head[..lineEnd]);
        requestBytes = headerEnd + terminatorLength;
        return true;
    }

    /// <summary>Builds the complete 418 response; every call returns a fresh array.</summary>
    public static byte[] BuildResponse()
    {
        return (byte[])Response.Clone();
    }
}
