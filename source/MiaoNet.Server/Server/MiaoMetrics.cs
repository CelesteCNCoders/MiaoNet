namespace MiaoNet.Server;

public struct MiaoMetrics
{
    public long TcpUploadByBytes { get; set; }

    public long TcpDownloadByBytes { get; set; }

    public long TcpUploadByPackets { get; set; }

    public long TcpDownloadByPackets { get; set; }

    public long SessionsCount { get; set; }

    public long PlayerFramesCoalesced { get; set; }

    public long PlayerFramesDropped { get; set; }

    public long PlayerFrameGaps { get; set; }

    public long WatchSceneTransfersReassembled { get; set; }
}
