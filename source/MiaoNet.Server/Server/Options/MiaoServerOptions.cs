namespace MiaoNet.Server;

public sealed class MiaoServerOptions
{
    public string ListenEndPoint { get; set; } = "0.0.0.0:21473";

    public int HandshakeTimeout { get; set; } = 6000;

    public int PingPeriod { get; set; } = 4000;

    public int HeartbeatTimeoutThreshold { get; set; } = 15000;

    public int DisconnectTimeout { get; set; } = 3000;

    public int RequestTimeout { get; set; } = 10000;

    public double SendBatchFrequency { get; set; } = 1.0;

    public int SendBatchSize { get; set; } = 1344;
}
