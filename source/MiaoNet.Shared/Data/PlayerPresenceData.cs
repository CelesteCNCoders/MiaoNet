namespace MiaoNet.Shared;

// Lightweight presence payload: where a player is plus their global status
// flags (paused, taking golden, chat open, interactions enabled...). No
// PlayerID here - pair with PlayerPresenceDataWithID in multi-player snapshots,
// or use the packet's own PlayerID in single-player notifications.
public readonly struct PlayerPresenceData : IRefBinarySerializable<PlayerPresenceData>
{
    public PlayerLocation Location { get; }

    public uint PlayerEpoch { get; }

    public uint PlayerSequence { get; }

    public PlayerGlobalFlags GlobalFlags { get; }

    public PlayerPresenceData(
        PlayerLocation location,
        uint playerEpoch,
        uint playerSequence,
        PlayerGlobalFlags globalFlags
    )
    {
        Location = location;
        PlayerEpoch = playerEpoch;
        PlayerSequence = playerSequence;
        GlobalFlags = globalFlags;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Location);
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequence);
        writer.Write((ushort)GlobalFlags);
    }

    public static PlayerPresenceData Deserialize(ref RefBinaryReader reader)
        => new(
            reader.Read<PlayerLocation>(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            (PlayerGlobalFlags)reader.ReadUInt16()
        );
}
