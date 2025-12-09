#if MIAO_SERVER
using MiaoNet.Server.Primitives;
#endif
using MiaoNet.Shared;

namespace MiaoNet.Shared;

// used to sync sth that's not so time-sensitive
// i.e. player died, player's hair is being blown, or player changed their sprite
public sealed class PacketPlayerStateFlags : IPacket<PacketPlayerStateFlags>
{
    public enum StateFlags : ushort
    {
        // TODO we may need sync die direction
        // so we should have a new type of packet
        PlayerDied = 1 << 0,
        PlayerRespawning = 1 << 1
    }

    public StateFlags Flags { get; }

    public PacketPlayerStateFlags(StateFlags flags)
    {
        Flags = flags;
    }

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write((ushort)Flags);

    public static PacketPlayerStateFlags Deserialize(ref RefBinaryReader reader)
        => new((StateFlags)reader.ReadUInt16());
}