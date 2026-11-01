namespace Celeste.Mod.CelesteNet.DataTypes;

/// <summary>
/// The three meta types which say who sent a packet. The shim stamps them on the sending side,
/// because the original server does it while it forwards (see CelesteNetPlayerSession.Filter).
/// </summary>
public abstract class MetaPlayerBaseType<T> : MetaType<T> where T : MetaPlayerBaseType<T>
{
    public DataPlayerInfo? Player;

    protected MetaPlayerBaseType() { }

    protected MetaPlayerBaseType(DataPlayerInfo? player)
    {
        Player = player;
    }

    public DataPlayerInfo ForcePlayer
    {
        get => Player ?? throw new InvalidDataException();
        set => Player = value;
    }

    public override void Read(CelesteNetBinaryReader reader)
        => reader.Data.TryGetRef(reader.Read7BitEncodedUInt(), out Player);

    public override void Write(CelesteNetBinaryWriter writer)
        => writer.Write7BitEncodedUInt(Player?.ID ?? uint.MaxValue);

    public static implicit operator DataPlayerInfo?(MetaPlayerBaseType<T> meta)
        => meta.Player;

    public static implicit operator uint(MetaPlayerBaseType<T> meta)
        => meta.Player?.ID ?? uint.MaxValue;
}

public sealed class MetaPlayerPrivateState : MetaPlayerBaseType<MetaPlayerPrivateState>
{
    static MetaPlayerPrivateState() => MetaID = "playerPrivSt";
    public MetaPlayerPrivateState() { }
    public MetaPlayerPrivateState(DataPlayerInfo? player) : base(player) { }
}

public sealed class MetaPlayerUpdate : MetaPlayerBaseType<MetaPlayerUpdate>
{
    static MetaPlayerUpdate() => MetaID = "playerUpd";
    public MetaPlayerUpdate() { }
    public MetaPlayerUpdate(DataPlayerInfo? player) : base(player) { }
}

public sealed class MetaPlayerPublicState : MetaPlayerBaseType<MetaPlayerPublicState>
{
    static MetaPlayerPublicState() => MetaID = "playerPubSt";
    public MetaPlayerPublicState() { }
    public MetaPlayerPublicState(DataPlayerInfo? player) : base(player) { }
}
