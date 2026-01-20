namespace MiaoNet.Shared;

public sealed class PacketPlayerPlayedAudio : IContextualPacket<PacketPlayerPlayedAudio>
{
    public PlayerPlayedAudio PlayerPlayedAudio { get; }

    public PacketPlayerPlayedAudio(PlayerPlayedAudio playerPlayedAudio)
    {
        PlayerPlayedAudio = playerPlayedAudio;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
    {
        writer.Write(PlayerPlayedAudio, context.PooledStringManager);
    }

    public static PacketPlayerPlayedAudio Deserialize(ref RefBinaryReader reader, IPacketSerializationContext context)
    {
        return new(reader.Read<PlayerPlayedAudio, PooledStringManager>(context.PooledStringManager));
    }
}