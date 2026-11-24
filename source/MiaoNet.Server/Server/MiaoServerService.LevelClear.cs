using System.Collections.Concurrent;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

public sealed partial class MiaoServerService
{
    // Deduplicate per account + ClearID across reconnects for the lifetime of this server process.
    private readonly ConcurrentDictionary<(int AuthID, Guid ClearID), PacketLevelClear> sharedClears = new();

    internal async Task HandlePacketAsync(MiaoClientConnection connection, PacketEnvelope envelope, PacketShareLevelClear packet)
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
            // Confirm back to the requester without re-broadcasting (e.g. retry after reconnect).
            if (connection.LevelClearShareLimiter.TryAcquire(Environment.TickCount64))
            {
                await connection.QueuePacketAsync(
                    new PacketLevelClear(previous.ChatChannelID, connection.ID, previous.DateTime, previous.Record)
                );
            }
            return;
        }

        if (!connection.LevelClearShareLimiter.TryAcquire(Environment.TickCount64))
            return;

        var notification = new PacketLevelClear(
            ServerChatChannel.AchievementsID,
            connection.ID,
            DateTime.UtcNow,
            packet.Record
        );
        if (!sharedClears.TryAdd(key, notification))
            return;

        logger.LogInformation(AppEvents.GameChat, "{player} shared a clear of {map}.", connection.Player.Info, packet.Record.MapSID);
        await BroadcastAsync(notification);
    }
}
