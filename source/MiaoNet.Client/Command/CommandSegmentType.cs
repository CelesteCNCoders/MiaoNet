namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Used to specify parts of command args
/// </summary>
public enum CommandSegmentType : byte
{
    Text,
    Emote,
    Player,
    PlayerSameChannel,
    PlayerSameMap,
    Channel,
    ChatChannelType,
    CommandName
}