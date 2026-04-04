namespace MiaoNet.Server;

public struct MiaoMetrics
{
    public long TcpUploadByBytes { get; set; }

    public long TcpDownloadByBytes { get; set; }

    public long TcpUploadByPackets { get; set; }

    public long TcpDownloadByPackets { get; set; }

    public long SessionsCount { get; set; }
}
