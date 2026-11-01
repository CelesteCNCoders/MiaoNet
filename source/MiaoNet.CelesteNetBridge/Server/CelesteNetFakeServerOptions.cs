namespace MiaoNet.CelesteNetBridge.Server;

public sealed class CelesteNetFakeServerOptions {
    /// <summary>Loopback address to listen on. Keep it a literal IP: the real client resolves "localhost" to ::1 first.</summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>Port to listen on. 0 picks a free port (useful in tests). CelesteNet's default is 17230.</summary>
    public int Port { get; init; } = 17230;

    /// <summary>Player id we tell the client it has. The first DataPlayerInfo we send is the client itself.</summary>
    public uint PlayerId { get; init; } = 1;

    public string PlayerName { get; init; } = "Player";

    /// <summary>
    /// Per-session identity. Each client must get a distinct id, otherwise every client thinks it is
    /// player 1 and treats the other player's frames as its own. Defaults to <see cref="PlayerId"/>.
    /// </summary>
    public Func<(uint Id, string Name)>? IdentityProvider { get; init; }

    /// <summary>The client joins this channel; without one it never reports a map.</summary>
    public string ChannelName { get; init; } = "main";

    public bool SendChannelList { get; init; } = true;

    public float TickRate { get; init; } = 60f;

    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Called with human readable progress lines. Never null inside the session.</summary>
    public Action<string>? Logger { get; init; }
}
