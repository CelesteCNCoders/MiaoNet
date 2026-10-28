// 验证客户端通关快照，并固定转发到全局频道。
using MiaoNet.Shared;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MiaoNet.Server;

public sealed partial class MiaoServerService
{
    // 连接重建后仍按账号与通关 ID 去重；缓存仅存在于本次服务进程。
    private readonly ConcurrentDictionary<(int AuthID, Guid ClearID), PacketLevelClear> sharedClears = new();

    private async Task HandlePacketAsync(MiaoClientConnection connection, PacketShareLevelClear packet)
    {
        if (!packet.Record.IsValid())
        {
            logger.LogWarning(AppEvents.GameChat, "Invalid clear record from {player}.", connection.Player.Info);
            await connection.DisconnectAsync(DisconnectReason.Kicked, "Invalid clear record.");
            return;
        }
        var key = (connection.Player.Info.AuthID, Guid.ParseExact(packet.Record.ClearID, "N"));
        if (sharedClears.TryGetValue(key, out var previous))
        {
            // 补发给请求者作为确认，不再次广播；使用重连后的连接 ID。
            if (connection.LevelClearShareLimiter.TryAcquire(Environment.TickCount64))
                await connection.QueuePacketAsync(new PacketLevelClear(connection.ID, previous.DateTime, previous.Record));
            return;
        }
        if (!connection.LevelClearShareLimiter.TryAcquire(Environment.TickCount64))
            return;

        var now = DateTime.UtcNow;
        var notification = new PacketLevelClear(connection.ID, now, packet.Record);
        if (!sharedClears.TryAdd(key, notification))
            return;
        logger.LogInformation(AppEvents.GameChat, "{player} shared a clear of {map}.", connection.Player.Info, packet.Record.MapSID);
        await BroadcastAsync(notification);
    }
}
