namespace MiaoNet.Server;

public sealed class MiaoMetricsService
{
    private long tcpUploadByBytes;
    private long tcpUploadByPackets;
    private long tcpDownloadByBytes;
    private long tcpDownloadByPackets;
    private long sessionsCount;
    private long playerFramesCoalesced;
    private long playerFramesDropped;
    private long playerFrameGaps;
    private long watchSceneTransfersReassembled;

    public MiaoMetrics Get() => new()
    {
        TcpUploadByBytes = Interlocked.Read(ref tcpUploadByBytes),
        TcpUploadByPackets = Interlocked.Read(ref tcpUploadByPackets),
        TcpDownloadByBytes = Interlocked.Read(ref tcpDownloadByBytes),
        TcpDownloadByPackets = Interlocked.Read(ref tcpDownloadByPackets),
        SessionsCount = Interlocked.Read(ref sessionsCount),
        PlayerFramesCoalesced = Interlocked.Read(ref playerFramesCoalesced),
        PlayerFramesDropped = Interlocked.Read(ref playerFramesDropped),
        PlayerFrameGaps = Interlocked.Read(ref playerFrameGaps),
        WatchSceneTransfersReassembled = Interlocked.Read(ref watchSceneTransfersReassembled),
    };

    public void RecordSession()
    {
        Interlocked.Increment(ref sessionsCount);
    }

    public void RecordPacketTcpUpload(int packetsCount, int bytes)
    {
        Interlocked.Add(ref tcpUploadByPackets, packetsCount);
        Interlocked.Add(ref tcpUploadByBytes, bytes);
    }

    public void RecordPacketTcpDownload(int packetsCount, int bytes)
    {
        Interlocked.Add(ref tcpDownloadByPackets, packetsCount);
        Interlocked.Add(ref tcpDownloadByBytes, bytes);
    }

    public void RecordPlayerFrameCoalesced()
        => Interlocked.Increment(ref playerFramesCoalesced);

    public void RecordPlayerFrameDropped()
        => Interlocked.Increment(ref playerFramesDropped);

    public void RecordPlayerFrameGap()
        => Interlocked.Increment(ref playerFrameGaps);

    public void RecordWatchSceneTransferReassembled()
        => Interlocked.Increment(ref watchSceneTransfersReassembled);
}
