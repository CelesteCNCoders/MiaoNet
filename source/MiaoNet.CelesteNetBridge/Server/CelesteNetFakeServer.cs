using System.Net;
using System.Net.Sockets;

namespace MiaoNet.CelesteNetBridge.Server;

/// <summary>
/// A CelesteNet server that is only real enough: it lets a stock CelesteNet client reach
/// <c>IsReady</c> on the loopback interface, keeps it alive, and then relays DataType frames.
/// It implements no chat, no player frames, no ghosts, no rooms, no auth.
/// </summary>
public sealed class CelesteNetFakeServer : IDisposable {
    private readonly CelesteNetFakeServerOptions options;
    private readonly CancellationTokenSource cts = new();
    private TcpListener? listener;
    private Task? acceptTask;
    private int disposed;

    public CelesteNetFakeServer(CelesteNetFakeServerOptions? options = null) {
        this.options = options ?? new CelesteNetFakeServerOptions();
    }

    /// <summary>The port actually bound. Only valid after <see cref="Start"/>.</summary>
    public int Port => listener is { } bound
        ? ((IPEndPoint)bound.LocalEndpoint).Port
        : throw new InvalidOperationException("the fake server is not listening yet");

    /// <summary>
    /// True when <see cref="CelesteNetFakeServerOptions.Port"/> was taken and the OS assigned another
    /// one instead. The caller must tell the client which port it actually got.
    /// </summary>
    public bool UsedFallbackPort { get; private set; }

    public bool IsRunning => acceptTask is { IsCompleted: false };

    public CelesteNetFakeServerOptions Options => options;

    /// <summary>Raised once per accepted client, before the handshake is answered.</summary>
    public event Action<CelesteNetFakeSession>? SessionStarted;

    private Action<string> Log => options.Logger ?? (_ => { });

    public void Start() {
        Priming.ThrowIfDisposed(disposed, this);

        listener = Bind(options.Host, options.Port);
        Log(UsedFallbackPort
            ? $"listening on {options.Host}:{Port} (port {options.Port} was busy)"
            : $"listening on {options.Host}:{Port}");

        TcpListener active = listener;
        acceptTask = Task.Run(() => AcceptLoopAsync(active, cts.Token));
    }

    /// <summary>
    /// Binds the preferred port, and asks the OS for any free port when it is taken. A busy port is
    /// not an error: the client is pointed at whatever we got, so only a broken loopback can stop us.
    /// </summary>
    private TcpListener Bind(string host, int preferred) {
        IPAddress address = IPAddress.Parse(host);
        try {
            TcpListener bound = new(address, preferred);
            bound.Start();
            return bound;
        } catch (SocketException e) when (preferred != 0) {
            Log($"port {preferred} unavailable ({e.SocketErrorCode}); asking the OS for a free port");
            UsedFallbackPort = true;

            TcpListener bound = new(address, 0);
            bound.Start();
            return bound;
        }
    }

    private async Task AcceptLoopAsync(TcpListener bound, CancellationToken token) {
        while (!token.IsCancellationRequested) {
            TcpClient client;
            try {
                client = await bound.AcceptTcpClientAsync(token).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                return;
            } catch (Exception e) {
                if (!token.IsCancellationRequested)
                    Log("accept failed: " + e.GetType().Name + ": " + e.Message);
                return;
            }

            (uint Id, string Name) identity = options.IdentityProvider?.Invoke()
                ?? (options.PlayerId, options.PlayerName);
            var session = new CelesteNetFakeSession(client, options, identity.Id, identity.Name);
            Log($"client connected from {client.Client.RemoteEndPoint}");

            try {
                SessionStarted?.Invoke(session);
            } catch (Exception e) {
                Log("SessionStarted handler threw: " + e.GetType().Name + ": " + e.Message);
            }

            session.Start();
        }
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        cts.Cancel();
        try {
            listener?.Stop();
        } catch (Exception) {
        }

        try {
            acceptTask?.Wait(TimeSpan.FromSeconds(2));
        } catch (Exception) {
        }
    }

    private static class Priming {
        public static void ThrowIfDisposed(int disposed, object owner)
            => ObjectDisposedException.ThrowIf(disposed != 0, owner);
    }
}
