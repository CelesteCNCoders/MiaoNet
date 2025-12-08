namespace MiaoNet.Shared;

public sealed class PacketEmote : PacketPlayerNotification, IPacket<PacketEmote>
{
    public EmoteData Emote { get; }

    public PacketEmote(int playerID, EmoteData emote) : base(playerID) 
        => Emote = emote;

    public override void Serialize(ref RefBinaryWriter writer)
    {
        base.Serialize(ref writer);
        writer.Write(Emote);
    }

    public static PacketEmote Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.Read<EmoteData>());
}

public sealed class PacketSendEmote : IPacket<PacketSendEmote>
{
    public EmoteData Emote { get; }

    public PacketSendEmote(EmoteData emote) 
        => Emote = emote;

    public static PacketSendEmote Deserialize(ref RefBinaryReader reader)
        => new(reader.Read<EmoteData>());

    public void Serialize(ref RefBinaryWriter writer)
        => writer.Write(Emote);
}