namespace MiaoNet.Shared;

public sealed class PacketEmote : IContextlessPacket<PacketEmote>
{
    public EmoteData Emote { get; }

    public PacketEmote(EmoteData emote)
        => Emote = emote;

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write(Emote);

    public static PacketEmote Deserialize(ref RefBinaryReader reader)
        => new(reader.Read<EmoteData>());
}
