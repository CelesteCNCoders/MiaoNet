using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class ChatComponent
{
    private readonly HashSet<(int PlayerID, string ClearID)> receivedClears = new();

    private void Context_LevelClearReceived(OnlinePlayer player, PacketLevelClear packet)
    {
        if (MiaoNetModule.Settings.LiveMode)
            return;
        if (!receivedClears.Add((player.ID, packet.Record.ClearID)))
            return;

        chatMessageBox.AddChatItem(
            new LevelClearChatItem(
                player.GetDisplayName(true, context.ShowAvatar),
                player.Info.Color,
                packet.DateTime,
                packet.Record
            ),
            packet.DateTime,
            packet.ChatChannelID
        );
    }
}
