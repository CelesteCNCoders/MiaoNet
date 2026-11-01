namespace Celeste.Mod.CelesteNet.DataTypes;

/// <summary>
/// One player's state — where they are and whether they are idle — which mods read to show the
/// other players in the player list.
/// </summary>
/// <remarks>
/// The shim declares it for the mods which bind to it rather than for itself: CollabUtils2 takes one
/// in its <c>CelesteNetPlayerListComponent.OnGetState</c> handler, and a method whose signature names
/// a type that cannot be resolved is a fatal <see cref="TypeLoadException"/> at JIT time rather than
/// something which simply never runs.
/// <para>
/// It is the original type instead of a stub, because the shim relays a mod's data types by their
/// DataID: a peer running the same mod reads what this side wrote.
/// </para>
/// </remarks>
public class DataPlayerState : DataType<DataPlayerState>
{
    static DataPlayerState() => DataID = "playerState";

    public override DataFlags DataFlags => DataFlags.Taskable;

    public DataPlayerInfo? Player;

    public string SID = "";
    public AreaMode Mode;
    public string Level = "";
    public bool Idle;
    public bool Interactive;

    public override MetaType[] GenerateMeta(DataContext context) =>
    [
        new MetaPlayerPrivateState(Player),
        new MetaBoundRef(DataPlayerInfo.DataID, Player?.ID ?? uint.MaxValue, true),
    ];

    public override void FixupMeta(DataContext context)
    {
        Player = Get<MetaPlayerPrivateState>(context);
        Get<MetaBoundRef>(context).ID = Player?.ID ?? uint.MaxValue;
    }

    protected override void Read(CelesteNetBinaryReader reader)
    {
        SID = reader.ReadNetString();
        Mode = (AreaMode)reader.ReadByte();
        Level = reader.ReadNetString();
        Idle = reader.ReadBoolean();
        Interactive = reader.ReadBoolean();
    }

    protected override void Write(CelesteNetBinaryWriter writer)
    {
        writer.WriteNetString(SID);
        writer.Write((byte)Mode);
        writer.WriteNetString(Level);
        writer.Write(Idle);
        writer.Write(Interactive);
    }

    public override string ToString()
        => $"#{Player?.ID ?? uint.MaxValue}: {SID}, {Idle}";
}
