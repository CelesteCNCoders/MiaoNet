namespace MiaoNet.Server;

public sealed class MiaoServerOptions
{
    public string ListenIPEndPoint { get; set; } = "0.0.0.0:21473";

    public int HandshakeTimeout { get; set; } = 3000;
}