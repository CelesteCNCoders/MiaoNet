namespace MiaoNet.Shared;

public sealed class PacketPlayerMapChangedResponse
    : IContextualPacket<PacketPlayerMapChangedResponse>
{
    public readonly struct Player : IContextualRefBinarySerializable<Player, PooledStringManager>
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

        public void Serialize(ref RefBinaryWriter writer, PooledStringManager pooledStringManager)
        {
            writer.Write(PlayerID);
            writer.Write(State, pooledStringManager);
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

        public static Player Deserialize(ref RefBinaryReader reader, PooledStringManager pooledStringManager)
            => new Player(
                reader.ReadInt32(),
                reader.Read<PlayerState, PooledStringManager>(pooledStringManager),
                reader.ReadBoolean() ? reader.Read<PlayerGraphicsInfo>() : null
            );
    }

    public Player[] PlayersInMap { get; }

    public PacketPlayerMapChangedResponse(Player[] playersInMap)
    {
        PlayersInMap = playersInMap;
    }

    public void Serialize(ref RefBinaryWriter writer, IPacketSerializationContext context)
        => writer.Write(PlayersInMap, context.PooledStringManager);

    public static PacketPlayerMapChangedResponse Deserialize(
        ref RefBinaryReader reader,
       IPacketSerializationContext context
    ) => new(reader.ReadArray<Player, PooledStringManager>(context.PooledStringManager));
}