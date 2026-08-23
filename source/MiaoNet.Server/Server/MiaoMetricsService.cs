namespace MiaoNet.Server;

public sealed class MiaoMetricsService
{
    private long tcpUploadByBytes;
    private long tcpUploadByPackets;
    private long tcpDownloadByBytes;
    private long tcpDownloadByPackets;
    private long sessionsCount;

    public MiaoMetrics Get() => new()
    {
        TcpUploadByBytes = Interlocked.Read(ref tcpUploadByBytes),
        TcpUploadByPackets = Interlocked.Read(ref tcpUploadByPackets),
        TcpDownloadByBytes = Interlocked.Read(ref tcpDownloadByBytes),
        TcpDownloadByPackets = Interlocked.Read(ref tcpDownloadByPackets),
        SessionsCount = Interlocked.Read(ref sessionsCount)
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
}