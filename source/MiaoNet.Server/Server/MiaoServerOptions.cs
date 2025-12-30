namespace MiaoNet.Server;

public sealed class MiaoServerOptions
{
    public string ListenIPEndPoint { get; set; } = "0.0.0.0:21473";

    public int HandshakeTimeout { get; set; } = 3000;

    public int PingPeriod { get; set; } = 4000;

    public int HeartbeatTimeoutThreshold { get; set; } = 10000;

    public required Version ExpectedVersion { get; set; }
}