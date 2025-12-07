namespace MiaoNet.Shared;

public sealed class PacketPlayerMapChangedResponse : IPacket<PacketPlayerMapChangedResponse>
{
    public readonly struct Player : IRefBinarySerializable<Player>
    {
        public int PlayerID { get; }

        public PlayerState State { get; }

        public PlayerGraphicsInfo? GraphicsInfo { get; }

        public Player(int playerID, PlayerState state, PlayerGraphicsInfo? graphicsInfo)
        {
            PlayerID = playerID;
            State = state;
            GraphicsInfo = graphicsInfo;
        }

        public void Serialize(ref RefBinaryWriter writer)
        {
            writer.Write(PlayerID);
            writer.Write(State);
            if (GraphicsInfo is not null)
            {
                writer.Write(true);
                writer.Write(GraphicsInfo);
            }
            else
            {
                writer.Write(false);
            }
        }

        public static Player Deserialize(ref RefBinaryReader reader)
            => new(
                reader.ReadInt32(),
                reader.Read<PlayerState>(),
                reader.ReadBoolean() ? reader.Read<PlayerGraphicsInfo>() : null
            );
    }

    public Player[] PlayersInMap { get; }

    public PacketPlayerMapChangedResponse(Player[] playersInMap)
    {
        PlayersInMap = playersInMap;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(PlayersInMap);
    }

    public static PacketPlayerMapChangedResponse Deserialize(ref RefBinaryReader reader)
        => new(reader.ReadArray<Player>());
}