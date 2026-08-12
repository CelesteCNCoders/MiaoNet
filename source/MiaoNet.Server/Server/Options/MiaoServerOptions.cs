using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class MiaoServerOptions
{
    public NetworkOptions Network { get; set; } = new() { ListenEndPoint = "0.0.0.0:21473" };

    public int HandshakeTimeout { get; set; } = 6000;

    public int PingPeriod { get; set; } = 4000;

    public int HeartbeatTimeoutThreshold { get; set; } = 15000;

    public int DisconnectTimeout { get; set; } = 3000;

    public required CertificateOptions Certificate { get; set; }

    public required AuthenticationOptions Authentication { get; set; }

    public required LocalizedOptions<AnnouncementsStrings> Announcements { get; set; }

    public string HttpListenerPrefix { get; set; } = "http://localhost:21474/";
}