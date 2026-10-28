// 通关分享使用专用消息；发送者及广播时间由服务端填写。
namespace MiaoNet.Shared;

public sealed class PacketShareLevelClear(LevelClearRecord record) : IContextlessPacket<PacketShareLevelClear>
{
    public LevelClearRecord Record { get; } = record;

    public void Serialize(ref RefBinaryWriter writer) => Record.Serialize(ref writer);
    public static PacketShareLevelClear Deserialize(ref RefBinaryReader reader)
        => new(LevelClearRecord.Deserialize(ref reader));
}

public sealed class PacketLevelClear(int playerID, DateTime dateTime, LevelClearRecord record)
    : IContextlessPacket<PacketLevelClear>
{
    public int PlayerID { get; } = playerID;
    public DateTime DateTime { get; } = dateTime;
    public LevelClearRecord Record { get; } = record;

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(PlayerID);
        writer.Write(DateTime);
        Record.Serialize(ref writer);
    }

    public static PacketLevelClear Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadInt32(), reader.ReadDateTime(), LevelClearRecord.Deserialize(ref reader));
}
