namespace MiaoNet.Shared;

public sealed class PacketShareLevelClear : IContextlessPacket<PacketShareLevelClear>
{
    public LevelClearRecord Record { get; }

    public PacketShareLevelClear(LevelClearRecord record)
    {
        Record = record;
    }

    public void Serialize(ref RefBinaryWriter writer) => Record.Serialize(ref writer);

    public static PacketShareLevelClear Deserialize(ref RefBinaryReader reader)
        => new(LevelClearRecord.Deserialize(ref reader));
}

public sealed class PacketLevelClear : IContextlessPacket<PacketLevelClear>
{
    public int ChatChannelID { get; }

    public int PlayerID { get; }

    public DateTime DateTime { get; }

    public LevelClearRecord Record { get; }

    public PacketLevelClear(int chatChannelID, int playerID, DateTime dateTime, LevelClearRecord record)
    {
        ChatChannelID = chatChannelID;
        PlayerID = playerID;
        DateTime = dateTime;
        Record = record;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write7BitEncodedInt(ChatChannelID);
        writer.Write7BitEncodedInt(PlayerID);
        writer.Write(DateTime);
        Record.Serialize(ref writer);
    }

    public static PacketLevelClear Deserialize(ref RefBinaryReader reader)
        => new(
            reader.Read7BitEncodedInt(),
            reader.Read7BitEncodedInt(),
            reader.ReadDateTime(),
            LevelClearRecord.Deserialize(ref reader)
        );
}
