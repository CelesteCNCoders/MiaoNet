using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

/// <summary>
/// 每 5 秒采样一次服务器指标(在线人数/频道数/包与字节速率/聊天数),
/// 保留最近约 1 小时(720 点)的时间序列, 供管理后台图表使用.
/// </summary>
public sealed class AdminMetricsSampler : BackgroundService
{
    public const int Capacity = 720;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    public readonly record struct Sample(
        long Time,
        int OnlinePlayers,
        int Channels,
        double UpPacketsPerSecond,
        double DownPacketsPerSecond,
        double UpBytesPerSecond,
        double DownBytesPerSecond,
        double ChatMessagesPerInterval,
        long Sessions,
        long ChatMessagesTotal
    );

    private readonly IMiaoServerService miaoServerService;
    private readonly MiaoMetricsService miaoMetricsService;
    private readonly AdminChatBuffer adminChatBuffer;
    private readonly ILogger<AdminMetricsSampler> logger;

    private readonly Sample[] samples = new Sample[Capacity];
    private readonly object sync = new();
    private int head;
    private int count;

    private readonly long startedTickCount = Environment.TickCount64;

    public AdminMetricsSampler(
        IMiaoServerService miaoServerService,
        MiaoMetricsService miaoMetricsService,
        AdminChatBuffer adminChatBuffer,
        ILogger<AdminMetricsSampler> logger
    )
    {
        this.miaoServerService = miaoServerService;
        this.miaoMetricsService = miaoMetricsService;
        this.adminChatBuffer = adminChatBuffer;
        this.logger = logger;
    }

    public long UptimeSeconds => (Environment.TickCount64 - startedTickCount) / 1000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);

        var previous = miaoMetricsService.Get();
        long previousChat = adminChatBuffer.TotalCount;

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var current = miaoMetricsService.Get();
                long currentChat = adminChatBuffer.TotalCount;
                double seconds = Interval.TotalSeconds;

                Sample sample = new(
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    miaoServerService.Players.Count,
                    miaoServerService.Channels.Count,
                    (current.TcpUploadByPackets - previous.TcpUploadByPackets) / seconds,
                    (current.TcpDownloadByPackets - previous.TcpDownloadByPackets) / seconds,
                    (current.TcpUploadByBytes - previous.TcpUploadByBytes) / seconds,
                    (current.TcpDownloadByBytes - previous.TcpDownloadByBytes) / seconds,
                    currentChat - previousChat,
                    current.SessionsCount,
                    currentChat
                );

                previous = current;
                previousChat = currentChat;

                lock (sync)
                {
                    samples[head] = sample;
                    head = (head + 1) % samples.Length;
                    if (count < samples.Length)
                        count++;
                }
            }
            catch (Exception e)
            {
                logger.LogError(AppEvents.Http, e, "Failed to sample admin metrics.");
            }
        }
    }

    /// <summary>获取当前快照与按时间升序的时间序列.</summary>
    public (Sample Current, List<Sample> Series) GetSnapshot()
    {
        lock (sync)
        {
            List<Sample> series = new(count);
            for (int i = 0; i < count; i++)
            {
                int index = (head - count + i + samples.Length) % samples.Length;
                series.Add(samples[index]);
            }
            Sample current = count > 0
                ? samples[(head - 1 + samples.Length) % samples.Length]
                : new Sample(
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    miaoServerService.Players.Count,
                    miaoServerService.Channels.Count,
                    0, 0, 0, 0, 0,
                    miaoMetricsService.Get().SessionsCount,
                    adminChatBuffer.TotalCount
                );
            return (current, series);
        }
    }
}
