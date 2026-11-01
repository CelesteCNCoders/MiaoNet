using System.Net.Sockets;
using System.Text;
using MiaoNet.CelesteNetBridge.Protocol;

namespace MiaoNet.UnitTest;

/// <summary>
/// A client that speaks the CelesteNet wire protocol the way the real client does, used to drive
/// <c>CelesteNetFakeServer</c> without launching Celeste.
/// </summary>
public sealed class CelesteNetTestClient : IDisposable {
    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly List<byte> pending = [];

    public CelesteNetTestClient(int port) {
        client = new TcpClient();
        client.NoDelay = true;
        client.Connect("127.0.0.1", port);
        stream = client.GetStream();
    }

    public static byte[] BuildTeapotRequest() {
        StringBuilder sb = new();
        sb.Append("TEAREQ /teapot HTTP/4.2\r\n");
        sb.Append("Connection: keep-alive\r\n");
        sb.Append("CelesteNet-TeapotVersion: 2\r\n");
        sb.Append("CelesteNet-ConnectionFeatures: \r\n");
        sb.Append("CelesteNet-Settings-MaxPacketSize: 2048\r\n");
        sb.Append("CelesteNet-Settings-MaxQueueSize: 1024\r\n");
        sb.Append("CelesteNet-Settings-MergeWindow: 15\r\n");
        sb.Append("CelesteNet-Settings-MaxHeartbeatDelay: 20\r\n");
        sb.Append("CelesteNet-Settings-HeartbeatInterval: 250\r\n");
        sb.Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    public async Task HandshakeAsync(TimeSpan? timeout = null) {
        using CancellationTokenSource cts = new(timeout ?? TimeSpan.FromSeconds(5));
        await stream.WriteAsync(BuildTeapotRequest(), cts.Token);

        string head = await ReadUntilAsync("\r\n\r\n", cts.Token);
        if (!head.StartsWith(CelesteNetHandshake.TeapotLine, StringComparison.Ordinal))
            throw new InvalidDataException("unexpected status line: " + head.Split("\r\n")[0]);

        // The response carries a non-empty body terminated by its own blank line; leaving it in the
        // stream would desync the frame reader.
        await ReadUntilAsync("\r\n\r\n", cts.Token);
    }

    private async Task<string> ReadUntilAsync(string terminator, CancellationToken token) {
        byte[] needle = Encoding.ASCII.GetBytes(terminator);
        while (true) {
            string current = Encoding.ASCII.GetString(pending.ToArray());
            int index = current.IndexOf(terminator, StringComparison.Ordinal);
            if (index >= 0) {
                int byteCount = Encoding.ASCII.GetByteCount(current[..(index + terminator.Length)]);
                byte[] result = pending.GetRange(0, byteCount).ToArray();
                pending.RemoveRange(0, byteCount);
                return Encoding.ASCII.GetString(result);
            }

            byte[] buffer = new byte[4096];
            int read = await stream.ReadAsync(buffer, token);
            if (read <= 0)
                throw new IOException("connection closed while waiting for " + terminator);
            pending.AddRange(buffer.AsSpan(0, read).ToArray());
        }
    }

    /// <summary>Reads one DataType frame payload.</summary>
    public async Task<byte[]> ReceiveAsync(TimeSpan? timeout = null) {
        using CancellationTokenSource cts = new(timeout ?? TimeSpan.FromSeconds(5));
        while (true) {
            if (CelesteNetFraming.TryRead(pending.ToArray(), out byte[] payload, out int consumed)) {
                pending.RemoveRange(0, consumed);
                return payload;
            }

            byte[] buffer = new byte[4096];
            int read = await stream.ReadAsync(buffer, cts.Token);
            if (read <= 0)
                throw new IOException("connection closed while waiting for a frame");
            pending.AddRange(buffer.AsSpan(0, read).ToArray());
        }
    }

    /// <summary>Sends one DataType payload, framed the way the real client does.</summary>
    public async Task SendAsync(byte[] payload) => await stream.WriteAsync(CelesteNetFraming.Wrap(payload));

    public async Task SendRawAsync(byte[] bytes) => await stream.WriteAsync(bytes);

    public void Dispose() {
        stream.Dispose();
        client.Dispose();
    }
}
