namespace MiaoNet.Shared;

// Lightweight presence payload: where a player is plus their global status
// flags (paused, taking golden, chat open, interactions enabled...). No
// PlayerID here - pair with PlayerPresenceDataWithID in multi-player snapshots,
// or use the packet's own PlayerID in single-player notifications.
public readonly struct PlayerPresenceData : IRefBinarySerializable<PlayerPresenceData>
{
    public PlayerLocation Location { get; }

    public PlayerGlobalFlags GlobalFlags { get; }

    public PlayerPresenceData(PlayerLocation location, PlayerGlobalFlags globalFlags)
    {
        Location = location;
        GlobalFlags = globalFlags;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Location);
        writer.Write((ushort)GlobalFlags);
    }

    public static PlayerPresenceData Deserialize(ref RefBinaryReader reader)
        => new(
            reader.Read<PlayerLocation>(),
            (PlayerGlobalFlags)reader.ReadUInt16()
        );
}
