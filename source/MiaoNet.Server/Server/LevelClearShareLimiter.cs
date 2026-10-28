// 每个连接独立限制通关分享频率，使用单调时钟避免系统时间调整。
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class LevelClearShareLimiter
{
    private long? lastShare;
    private readonly object sync = new();

    public bool TryAcquire(long milliseconds)
    {
        lock (sync)
        {
            if (lastShare is long last && milliseconds - last < LevelClearRecord.ShareCooldownSeconds * 1000L)
                return false;
            lastShare = milliseconds;
            return true;
        }
    }
}
