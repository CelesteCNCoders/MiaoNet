namespace MiaoNet.Shared;

[Flags]
public enum WatchPersistentSceneFlags : byte
{
    None = 0,
    Cassette = 1 << 0,
    HeartGem = 1 << 1,
    HitCheckpoint = 1 << 2,
    HasRespawnPoint = 1 << 3,
    CassetteGhost = 1 << 4,
    HeartGemGhost = 1 << 5,
    FakeHeart = 1 << 6,
}

public sealed class WatchPersistentSceneState : IRefBinarySerializable<WatchPersistentSceneState>
{
    private const byte SummitGemMask = 0b0011_1111;
    private const WatchPersistentSceneFlags AllFlags =
        WatchPersistentSceneFlags.Cassette
        | WatchPersistentSceneFlags.HeartGem
        | WatchPersistentSceneFlags.HitCheckpoint
        | WatchPersistentSceneFlags.HasRespawnPoint
        | WatchPersistentSceneFlags.CassetteGhost
        | WatchPersistentSceneFlags.HeartGemGhost
        | WatchPersistentSceneFlags.FakeHeart;

    public WatchPersistentSceneFlags Flags { get; }

    public byte SummitGems { get; }

    public Vector2? RespawnPoint { get; }

    public IReadOnlyCollection<int> DoNotLoadIDs { get; }

    public IReadOnlyCollection<int> StrawberryIDs { get; }

    public IReadOnlyCollection<int> GhostStrawberryIDs { get; }

    public WatchPersistentSceneState(
        WatchPersistentSceneFlags flags,
        byte summitGems,
        Vector2? respawnPoint,
        IReadOnlyCollection<int> doNotLoadIDs,
        IReadOnlyCollection<int> strawberryIDs,
        IReadOnlyCollection<int> ghostStrawberryIDs
    )
    {
        Flags = flags;
        SummitGems = summitGems;
        RespawnPoint = respawnPoint;
        DoNotLoadIDs = doNotLoadIDs;
        StrawberryIDs = strawberryIDs;
        GhostStrawberryIDs = ghostStrawberryIDs;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((byte)Flags);
        writer.Write(SummitGems);
        if (Flags.HasFlag(WatchPersistentSceneFlags.HasRespawnPoint))
            writer.Write(RespawnPoint!.Value);
        WriteIDs(ref writer, DoNotLoadIDs);
        WriteIDs(ref writer, StrawberryIDs);
        WriteIDs(ref writer, GhostStrawberryIDs);
    }

    public static WatchPersistentSceneState Deserialize(ref RefBinaryReader reader)
    {
        WatchPersistentSceneFlags flags = (WatchPersistentSceneFlags)reader.ReadByte();
        byte summitGems = reader.ReadByte();
        Vector2? respawnPoint = flags.HasFlag(WatchPersistentSceneFlags.HasRespawnPoint)
            ? reader.ReadVector2()
            : null;
        return new(
            flags,
            summitGems,
            respawnPoint,
            ReadIDs(ref reader),
            ReadIDs(ref reader),
            ReadIDs(ref reader)
        );
    }

    public byte[] ToPayload()
    {
        using MemoryStream stream = new();
        RefBinaryWriter writer = new(stream);
        Serialize(ref writer);
        return stream.ToArray();
    }

    public static bool TryFromPayload(
        ReadOnlySpan<byte> payload,
        out WatchPersistentSceneState? state
    )
    {
        try
        {
            RefBinaryReader reader = new(payload);
            state = Deserialize(ref reader);
            return reader.BytesLeft == 0 && state.IsValid();
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException
            or IndexOutOfRangeException
            or InvalidDataException)
        {
            state = null;
            return false;
        }
    }

    public bool IsValid()
        => (Flags & ~AllFlags) == 0
            && (SummitGems & ~SummitGemMask) == 0
            && Flags.HasFlag(WatchPersistentSceneFlags.HasRespawnPoint) == RespawnPoint.HasValue
            && (!RespawnPoint.HasValue
                || (float.IsFinite(RespawnPoint.Value.X) && float.IsFinite(RespawnPoint.Value.Y)))
            && AreValidIDs(DoNotLoadIDs)
            && AreValidIDs(StrawberryIDs)
            && AreValidIDs(GhostStrawberryIDs);

    private static bool AreValidIDs(IReadOnlyCollection<int> ids)
        => ids.Count <= ushort.MaxValue
            && ids.All(id => id >= 0)
            && ids.Distinct().Count() == ids.Count;

    private static void WriteIDs(ref RefBinaryWriter writer, IReadOnlyCollection<int> ids)
    {
        if (ids.Count > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(ids));

        writer.Write((ushort)ids.Count);
        foreach (int id in ids)
            writer.Write(id);
    }

    private static int[] ReadIDs(ref RefBinaryReader reader)
    {
        int count = reader.ReadUInt16();
        if (count > reader.BytesLeft / sizeof(int))
            throw new InvalidDataException("Persistent scene state contains a truncated ID list.");
        int[] ids = new int[count];
        for (int index = 0; index < count; index++)
            ids[index] = reader.ReadInt32();
        return ids;
    }
}
