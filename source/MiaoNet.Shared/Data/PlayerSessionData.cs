#if MIAO_CLIENT
using SessionCoreModes = Celeste.Session.CoreModes;
#elif MIAO_SERVER
using MiaoNet.Server.Primitives;
using SessionCoreModes = System.Int32;
#endif

using DataEntityID = MiaoNet.Shared.PlayerSessionData.StringIntPair;
using DataSessionCounter = MiaoNet.Shared.PlayerSessionData.StringIntPair;

namespace MiaoNet.Shared;

public sealed partial class PlayerSessionData : IRefBinarySerializable<PlayerSessionData>
{
    [Flags]
    public enum SessionDataFlags : ushort
    {
        None = 0,
        HasRespawnPoint = 1 << 0,
        HasStringFlags = 1 << 1,
        HasLevelStringFlags = 1 << 2,
        HasStrawberries = 1 << 3,
        HasDoNotLoad = 1 << 4,
        HasKeys = 1 << 5,
        HasCounters = 1 << 6,
        HasStartCheckpoint = 1 << 7,
        HasColorGrade = 1 << 8,
    }

    [Flags]
    public enum SessionFlags : ushort
    {
        None = 0,
        FirstLevel = 1 << 0,
        Cassette = 1 << 1,
        HeartGem = 1 << 2,
        Dreaming = 1 << 3,
        GrabbedGolden = 1 << 4,
        HitCheckpoint = 1 << 5
    }

    public SessionFlags Flags { get; }

    // this is not in session
    // i think this is not suitable at here...
    public Vector2 Position { get; }

    public Vector2? RespawnPoint { get; }

    public IReadOnlyCollection<string> StringFlags { get; }

    public IReadOnlyCollection<string> LevelStringFlags { get; }

    public IReadOnlyCollection<DataEntityID> Strawberries { get; }

    public IReadOnlyCollection<DataEntityID> DoNotLoad { get; }

    public IReadOnlyCollection<DataEntityID> Keys { get; }

    public IReadOnlyCollection<DataSessionCounter> Counters { get; }

    public string? StartCheckpoint { get; }

    public string? ColorGrade { get; }

    public ushort SummitGems { get; }

    public float LightingAlphaAdd { get; }

    public float BloomBaseAdd { get; }

    public float DarkRoomAlpha { get; }

    public long Time { get; }

    public SessionCoreModes CoreMode { get; }

    #region a huge ctor
    // no one likes a huge ctor though
    public PlayerSessionData(
        Vector2 position,
        Vector2? respawnPoint,
        IReadOnlyCollection<string> stringFlags,
        IReadOnlyCollection<string> levelStringFlags,
        IReadOnlyCollection<DataEntityID> strawberries,
        IReadOnlyCollection<DataEntityID> doNotLoad,
        IReadOnlyCollection<DataEntityID> keys,
        IReadOnlyCollection<DataSessionCounter> counters,
        string? startCheckpoint,
        string? colorGrade,
        ushort summitGems,
        SessionFlags flags,
        float lightingAlphaAdd,
        float bloomBaseAdd,
        float darkRoomAlpha,
        long time,
        SessionCoreModes coreMode
    )
    {
        Position = position;
        RespawnPoint = respawnPoint;
        StringFlags = stringFlags;
        LevelStringFlags = levelStringFlags;
        Strawberries = strawberries;
        DoNotLoad = doNotLoad;
        Keys = keys;
        Counters = counters;
        StartCheckpoint = startCheckpoint;
        ColorGrade = colorGrade;
        SummitGems = summitGems;
        Flags = flags;
        LightingAlphaAdd = lightingAlphaAdd;
        BloomBaseAdd = bloomBaseAdd;
        DarkRoomAlpha = darkRoomAlpha;
        Time = time;
        CoreMode = coreMode;
    }
    #endregion

#if MIAO_CLIENT

    public Session CreateSession(AreaKey areaKey, string room)
    {
        Session session = new(areaKey);
        session.Level = room;

        session.FirstLevel = Flags.HasFlag(SessionFlags.FirstLevel);
        session.Cassette = Flags.HasFlag(SessionFlags.Cassette);
        session.HeartGem = Flags.HasFlag(SessionFlags.HeartGem);
        session.Dreaming = Flags.HasFlag(SessionFlags.Dreaming);
        session.GrabbedGolden = Flags.HasFlag(SessionFlags.GrabbedGolden);
        session.HitCheckpoint = Flags.HasFlag(SessionFlags.HitCheckpoint);

        session.RespawnPoint = RespawnPoint;
        session.Flags = StringFlags.ToHashSet();
        session.LevelFlags = LevelStringFlags.ToHashSet();
        session.Strawberries = Strawberries.Select<DataEntityID, EntityID>(p => p).ToHashSet();
        session.DoNotLoad = DoNotLoad.Select<DataEntityID, EntityID>(p => p).ToHashSet();
        session.Keys = Keys.Select<DataEntityID, EntityID>(p => p).ToHashSet();
        session.Counters = Counters.Select<DataSessionCounter, Session.Counter>(p => p).ToList();

        session.StartCheckpoint = StartCheckpoint;
        session.ColorGrade = ColorGrade;
        session.SummitGems = [
            (SummitGems & 0b000001) != 0,
            (SummitGems & 0b000010) != 0,
            (SummitGems & 0b000100) != 0,
            (SummitGems & 0b001000) != 0,
            (SummitGems & 0b010000) != 0,
            (SummitGems & 0b100000) != 0,
        ];
        session.LightingAlphaAdd = LightingAlphaAdd;
        session.BloomBaseAdd = BloomBaseAdd;
        session.DarkRoomAlpha = DarkRoomAlpha;
        session.Time = Time;
        session.CoreMode = CoreMode;

        return session;
    }

    public static PlayerSessionData CreateFrom(Session session, Vector2 position)
    {
        SessionFlags flags = SessionFlags.None;
        if (session.FirstLevel) flags |= SessionFlags.FirstLevel;
        if (session.Cassette) flags |= SessionFlags.Cassette;
        if (session.HeartGem) flags |= SessionFlags.HeartGem;
        if (session.Dreaming) flags |= SessionFlags.Dreaming;
        if (session.GrabbedGolden) flags |= SessionFlags.GrabbedGolden;
        if (session.HitCheckpoint) flags |= SessionFlags.HitCheckpoint;

        ushort summitGems =
            (ushort)(
                (session.SummitGems[0] ? 0b000001 : 0) |
                (session.SummitGems[1] ? 0b000010 : 0) |
                (session.SummitGems[2] ? 0b000100 : 0) |
                (session.SummitGems[3] ? 0b001000 : 0) |
                (session.SummitGems[4] ? 0b010000 : 0) |
                (session.SummitGems[5] ? 0b100000 : 0)
            );

        return new PlayerSessionData(
            position,
            respawnPoint: session.RespawnPoint,
            stringFlags: session.Flags,
            levelStringFlags: session.LevelFlags,
            strawberries: session.Strawberries.Select<EntityID, DataEntityID>(id => id).ToList(),
            doNotLoad: session.DoNotLoad.Select<EntityID, DataEntityID>(id => id).ToList(),
            keys: session.Keys.Select<EntityID, DataEntityID>(id => id).ToList(),
            counters: session.Counters.Select<Session.Counter, DataSessionCounter>(c => c).ToList(),
            startCheckpoint: session.StartCheckpoint,
            colorGrade: session.ColorGrade,
            summitGems: summitGems,
            flags: flags,
            lightingAlphaAdd: session.LightingAlphaAdd,
            bloomBaseAdd: session.BloomBaseAdd,
            darkRoomAlpha: session.DarkRoomAlpha,
            time: session.Time,
            coreMode: session.CoreMode
        );
    }
#endif

    public void Serialize(ref RefBinaryWriter writer)
    {
        SessionDataFlags dataFlags = SessionDataFlags.None;
        if (RespawnPoint.HasValue) dataFlags |= SessionDataFlags.HasRespawnPoint;
        if (StringFlags.Count > 0) dataFlags |= SessionDataFlags.HasStringFlags;
        if (LevelStringFlags.Count > 0) dataFlags |= SessionDataFlags.HasLevelStringFlags;
        if (Strawberries.Count > 0) dataFlags |= SessionDataFlags.HasStrawberries;
        if (DoNotLoad.Count > 0) dataFlags |= SessionDataFlags.HasDoNotLoad;
        if (Keys.Count > 0) dataFlags |= SessionDataFlags.HasKeys;
        if (Counters.Count > 0) dataFlags |= SessionDataFlags.HasCounters;
        if (StartCheckpoint is not null) dataFlags |= SessionDataFlags.HasStartCheckpoint;
        if (ColorGrade is not null) dataFlags |= SessionDataFlags.HasColorGrade;

        writer.Write((ushort)dataFlags);
        writer.Write((ushort)Flags);

        writer.Write(Position);

        writer.Write(SummitGems);
        writer.Write(LightingAlphaAdd);
        writer.Write(BloomBaseAdd);
        writer.Write(DarkRoomAlpha);
        writer.Write(Time);
        writer.Write((byte)CoreMode);

        if (dataFlags.HasFlag(SessionDataFlags.HasRespawnPoint)) writer.Write((Vector2)RespawnPoint!);
        if (dataFlags.HasFlag(SessionDataFlags.HasStringFlags)) writer.Write(StringFlags);
        if (dataFlags.HasFlag(SessionDataFlags.HasLevelStringFlags)) writer.Write(LevelStringFlags);
        if (dataFlags.HasFlag(SessionDataFlags.HasStrawberries)) writer.Write(Strawberries);
        if (dataFlags.HasFlag(SessionDataFlags.HasDoNotLoad)) writer.Write(DoNotLoad);
        if (dataFlags.HasFlag(SessionDataFlags.HasKeys)) writer.Write(Keys);
        if (dataFlags.HasFlag(SessionDataFlags.HasCounters)) writer.Write(Counters);
        if (dataFlags.HasFlag(SessionDataFlags.HasStartCheckpoint)) writer.Write(StartCheckpoint!);
        if (dataFlags.HasFlag(SessionDataFlags.HasColorGrade)) writer.Write(ColorGrade!);
    }

    public static PlayerSessionData Deserialize(ref RefBinaryReader reader)
    {
        var dataFlags = (SessionDataFlags)reader.ReadUInt16();
        var flags = (SessionFlags)reader.ReadUInt16();

        Vector2 position = reader.ReadVector2();

        var summitGems = reader.ReadUInt16();
        var lightingAlphaAdd = reader.ReadSingle();
        var bloomBaseAdd = reader.ReadSingle();
        var darkRoomAlpha = reader.ReadSingle();
        var time = reader.ReadInt64();
        var coreMode = (SessionCoreModes)reader.ReadByte();

        Vector2? respawnPoint = dataFlags.HasFlag(SessionDataFlags.HasRespawnPoint)
            ? reader.ReadVector2()
            : null;

        IReadOnlyCollection<string> stringFlags = dataFlags.HasFlag(SessionDataFlags.HasStringFlags)
            ? reader.ReadStringArray()
            : Array.Empty<string>();

        IReadOnlyCollection<string> levelStringFlags = dataFlags.HasFlag(SessionDataFlags.HasLevelStringFlags)
            ? reader.ReadStringArray()
            : Array.Empty<string>();

        IReadOnlyCollection<DataEntityID> strawberries = dataFlags.HasFlag(SessionDataFlags.HasStrawberries)
            ? reader.ReadArray<DataEntityID>()
            : Array.Empty<DataEntityID>();

        IReadOnlyCollection<DataEntityID> doNotLoad = dataFlags.HasFlag(SessionDataFlags.HasDoNotLoad)
            ? reader.ReadArray<DataEntityID>()
            : Array.Empty<DataEntityID>();

        IReadOnlyCollection<DataEntityID> keys = dataFlags.HasFlag(SessionDataFlags.HasKeys)
            ? reader.ReadArray<DataEntityID>()
            : Array.Empty<DataEntityID>();

        IReadOnlyCollection<DataSessionCounter> counters = dataFlags.HasFlag(SessionDataFlags.HasCounters)
            ? reader.ReadArray<DataSessionCounter>()
            : Array.Empty<DataSessionCounter>();

        string? startCheckpoint = dataFlags.HasFlag(SessionDataFlags.HasStartCheckpoint)
            ? reader.ReadString()
            : null;

        string? colorGrade = dataFlags.HasFlag(SessionDataFlags.HasColorGrade)
            ? reader.ReadString()
            : null;

        return new PlayerSessionData(
            position,
            respawnPoint: respawnPoint,
            stringFlags: stringFlags,
            levelStringFlags: levelStringFlags,
            strawberries: strawberries,
            doNotLoad: doNotLoad,
            keys: keys,
            counters: counters,
            startCheckpoint: startCheckpoint,
            colorGrade: colorGrade,
            summitGems: summitGems,
            flags: flags,
            lightingAlphaAdd: lightingAlphaAdd,
            bloomBaseAdd: bloomBaseAdd,
            darkRoomAlpha: darkRoomAlpha,
            time: time,
            coreMode: coreMode
        );
    }
}