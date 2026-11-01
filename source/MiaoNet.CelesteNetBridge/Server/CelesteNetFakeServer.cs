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
    private readonly TcpListener listener;
    private readonly CancellationTokenSource cts = new();
    private Task? acceptTask;
    private int disposed;

    public CelesteNetFakeServer(CelesteNetFakeServerOptions? options = null) {
        this.options = options ?? new CelesteNetFakeServerOptions();
        listener = new TcpListener(IPAddress.Parse(this.options.Host), this.options.Port);
    }

    /// <summary>The port actually bound (useful when <see cref="CelesteNetFakeServerOptions.Port"/> was 0).</summary>
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public bool IsRunning => acceptTask is { IsCompleted: false };

    public CelesteNetFakeServerOptions Options => options;

    /// <summary>Raised once per accepted client, before the handshake is answered.</summary>
    public event Action<CelesteNetFakeSession>? SessionStarted;

    private Action<string> Log => options.Logger ?? (_ => { });

    public void Start() {
        Priming.ThrowIfDisposed(disposed, this);

        listener.Start();
        Log($"listening on {options.Host}:{Port}");

        acceptTask = Task.Run(() => AcceptLoopAsync(cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken token) {
        while (!token.IsCancellationRequested) {
            TcpClient client;
            try {
                client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
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
            listener.Stop();
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
