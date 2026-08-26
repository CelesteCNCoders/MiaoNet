namespace MiaoNet.Shared;

public enum WatchEntityKind : ushort
{
    None = 0,
    Spring = 1,
    PersistentSession = 2,
    Checkpoint = 3,
    SummitCheckpoint = 4,
    WingedStrawberry = 5,
    Refill = 6,
    FlyFeather = 7,
    Booster = 8,
    Bumper = 9,
    Cloud = 10,
    DashSwitch = 11,
    TempleGate = 12,
    CrumblePlatform = 13,
    CoreMode = 14,
    HeartGemDoor = 15,
    FakeHeart = 16,
    MovingSolid = 17,
    DashBlock = 18,
    StrawberrySeed = 19,
    BounceBlock = 20,
    PeriodicPlatform = 21,
    CassetteBlock = 22,
    SwitchGate = 23,
    ClutterSystem = 24,
    DoorMechanism = 25,
    Key = 26,
    LockBlock = 27,
    TheoCrystal = 28,
    Glider = 29,
    TheoCrystalPedestal = 30,
    BadelineBoost = 31,
    FlingBird = 32,
    WallBooster = 33,
    Torch = 34,
    TempleCrackedBlock = 35,
    TempleBigEyeball = 36,
    StaticSpinner = 37,
    TriggerSpikes = 38,
    FireBall = 39,
    Lava = 40,
    BadelineOldsite = 41,
    Snowball = 42,
    Puffer = 43,
    AngryOshiro = 44,
    SeekerSystem = 45,
    SeekerBarrier = 46,
    PlayerSeeker = 47,
    FinalBoss = 48,
    FinalBossShot = 49,
    FinalBossBeam = 50,
    FinalBossMovingBlock = 51,
    ReflectionTentacles = 52,
}

public enum WatchFinalBossBeamPhase : byte
{
    Charging = 0,
    Active = 1,
    Dissipating = 2,
}

public enum WatchFinalBossAnimation : byte
{
    Idle = 0,
    Laugh = 1,
    Attack1Begin = 2,
    Attack1Recoil = 3,
    GetHit = 4,
    PretendDead = 5,
    Attack1Loop = 6,
    Attack2Begin = 7,
    Attack2Aim = 8,
    Attack2Lock = 9,
    Attack2Recoil = 10,
    Star = 11,
    RecoverHit = 12,
    ScaredIdle = 13,
    ScaredTransition = 14,
    Calm = 15,
    LookUp = 16,
    LookingUp = 17,
    Unknown = byte.MaxValue,
}

public enum WatchMovingSolidType : byte
{
    ZipMover = 0,
    SwapBlock = 1,
    MoveBlock = 2,
    FallingBlock = 3,
    CrushBlock = 4,
    BounceBlock = 5,
    SinkingPlatform = 6,
    FloatySpaceBlock = 7,
    DreamBlock = 8,
    GoldenBlock = 9,
    GlassBlock = 10,
    StarJumpBlock = 11,
}

public enum WatchEntityPhase : byte
{
    Ready = 0,
    Active = 1,
    Cooldown = 2,
    Gone = 3,
    Returning = 4,
}

public enum WatchSnowballPhase : byte
{
    Active = 0,
    Broken = 1,
}

public enum WatchPufferPhase : byte
{
    Idle = 0,
    Hit = 1,
    Gone = 2,
}

public enum WatchAngryOshiroPhase : byte
{
    Chase = 0,
    ChargeUp = 1,
    Attack = 2,
    Dummy = 3,
    Waiting = 4,
    Hurt = 5,
}

public enum WatchSeekerForm : byte
{
    Statue = 0,
    Hatching = 1,
    Seeker = 2,
}

public enum WatchSeekerPhase : byte
{
    Idle = 0,
    Patrol = 1,
    Spotted = 2,
    Attack = 3,
    Stunned = 4,
    Skidding = 5,
    Regenerate = 6,
    Returned = 7,
}

public enum WatchHoldablePhase : byte
{
    Idle = 0,
    Carried = 1,
    Thrown = 2,
    Moving = 3,
    Flying = 4,
    Destroying = 5,
    Gone = 6,
}

public enum WatchWingedStrawberryState : byte
{
    Present = 0,
    FlyingAway = 1,
    Absent = 2,
}

public enum WatchStrawberrySeedPhase : byte
{
    Ready = 0,
    Following = 1,
    Returning = 2,
    Combining = 3,
}

public enum WatchEntityStateMode : byte
{
    None = 0,
    Patch = 1,
    Replace = 2,
}

public readonly struct WatchEntityKey : IRefBinarySerializable<WatchEntityKey>, IEquatable<WatchEntityKey>
{
    public WatchEntityKind Kind { get; }

    public int EntityID { get; }

    public ushort SubID { get; }

    public WatchEntityKey(WatchEntityKind kind, int entityID, ushort subID = 0)
    {
        Kind = kind;
        EntityID = entityID;
        SubID = subID;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write((ushort)Kind);
        writer.Write(EntityID);
        writer.Write(SubID);
    }

    public static WatchEntityKey Deserialize(ref RefBinaryReader reader)
        => new((WatchEntityKind)reader.ReadUInt16(), reader.ReadInt32(), reader.ReadUInt16());

    public bool Equals(WatchEntityKey other)
        => Kind == other.Kind && EntityID == other.EntityID && SubID == other.SubID;

    public override bool Equals(object? obj)
        => obj is WatchEntityKey other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Kind, EntityID, SubID);

    public static bool operator ==(WatchEntityKey left, WatchEntityKey right)
        => left.Equals(right);

    public static bool operator !=(WatchEntityKey left, WatchEntityKey right)
        => !left.Equals(right);
}

public readonly struct WatchEntityState : IRefBinarySerializable<WatchEntityState>
{
    private readonly byte[] payload;

    public WatchEntityKey Key { get; }

    public ReadOnlyMemory<byte> Payload => payload;

    public WatchEntityState(WatchEntityKey key, ReadOnlySpan<byte> payload)
    {
        Key = key;
        this.payload = payload.ToArray();
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Key);
        WritePayload(ref writer, Payload.Span);
    }

    public static WatchEntityState Deserialize(ref RefBinaryReader reader)
        => new(reader.Read<WatchEntityKey>(), ReadPayload(ref reader));

    internal static void WritePayload(ref RefBinaryWriter writer, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(payload));

        writer.Write((ushort)payload.Length);
        writer.WriteSpan(payload);
    }

    internal static byte[] ReadPayload(ref RefBinaryReader reader)
        => reader.ReadSpan(reader.ReadUInt16()).ToArray();
}

public readonly struct WatchEntityEvent : IRefBinarySerializable<WatchEntityEvent>
{
    private readonly byte[] payload;

    public WatchEntityKey Key { get; }

    public byte EventID { get; }

    public ReadOnlyMemory<byte> Payload => payload;

    public WatchEntityEvent(WatchEntityKey key, byte eventID, ReadOnlySpan<byte> payload)
    {
        Key = key;
        EventID = eventID;
        this.payload = payload.ToArray();
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Key);
        writer.Write(EventID);
        WatchEntityState.WritePayload(ref writer, Payload.Span);
    }

    public static WatchEntityEvent Deserialize(ref RefBinaryReader reader)
        => new(
            reader.Read<WatchEntityKey>(),
            reader.ReadByte(),
            WatchEntityState.ReadPayload(ref reader)
        );
}
