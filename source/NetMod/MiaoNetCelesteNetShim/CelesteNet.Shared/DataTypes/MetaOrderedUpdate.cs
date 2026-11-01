namespace Celeste.Mod.CelesteNet.DataTypes;

/// <summary>
/// Says "this replaces the previous packet of my type from the same player", which is the second
/// meta type of every packet a mod copies from <c>DataPlayerFrame</c>: a frame stream sends faster
/// than a datagram can be delivered, and the original drops the ones that arrive out of order.
/// </summary>
/// <remarks>
/// <see cref="UpdateID"/> stays unset on this side, which is what the original transport does for
/// every packet it did not receive over UDP: it is filled from the datagram's own counter, and only
/// a transport which can reorder has anything to drop. MiaoNet delivers a frame batch in order, so
/// one is never stale and the check never has to run.
/// </remarks>
public class MetaOrderedUpdate : MetaType<MetaOrderedUpdate>
{
    static MetaOrderedUpdate() => MetaID = "ordered";

    public uint ID;
    public byte? UpdateID;

    public MetaOrderedUpdate() { }
    public MetaOrderedUpdate(uint id) => ID = id;

    public override void Read(CelesteNetBinaryReader reader) => ID = reader.Read7BitEncodedUInt();
    public override void Write(CelesteNetBinaryWriter writer) => writer.Write7BitEncodedUInt(ID);
}
