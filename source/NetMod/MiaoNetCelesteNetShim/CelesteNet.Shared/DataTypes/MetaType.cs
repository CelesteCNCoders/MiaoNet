namespace Celeste.Mod.CelesteNet.DataTypes;

public abstract class MetaType
{
    // No MetaID field here: the original declares it on MetaType<T> only, and the id which goes
    // on the wire always comes from the registry. A field on this base class would also be picked
    // up by the scanner for non generic subclasses such as MetaUnparsed, which must stay unknown.
    public virtual void Read(CelesteNetBinaryReader reader) { }
    public virtual void Write(CelesteNetBinaryWriter writer) { }

    /// <summary>
    /// The id written on the wire. It is looked up in the registry, which is built by scanning
    /// <see cref="MetaType{T}.MetaID"/>, so a meta type can never write an id which the other side
    /// does not register it under.
    /// </summary>
    public virtual string GetTypeID(DataContext context)
        => context.MetaTypeToID.GetValueOrDefault(GetType(), "");
}

public abstract class MetaType<T> : MetaType where T : MetaType<T>
{
    /// <summary>
    /// The registered id of <typeparamref name="T"/>. A concrete meta type must <b>assign</b> this
    /// in its own static constructor (<c>static MetaFoo() { MetaID = "foo"; }</c>).
    /// <para>
    /// Never declare a field of the same name in a subclass: the original assigns the inherited
    /// field, and a <c>new</c> field only hides the one the scanner reads, so the packet would be
    /// written under one id and looked up under another.
    /// </para>
    /// </summary>
    public static string MetaID;

    static MetaType() => MetaID = typeof(T).Name;
}

/// <summary>A meta type this side does not know about; the raw bytes are kept as-is.</summary>
public sealed class MetaUnparsed : MetaType
{
    public string InnerID = "";
    public byte[] InnerData = [];
}

public sealed class MetaUpdateContext : IDisposable
{
    public readonly DataContext Context;
    public readonly DataType Data;
    public MetaUpdateContext(DataContext context, DataType data)
    {
        Context = context;
        Data = data;
        Data.Meta = data.GenerateMeta(Context);
    }
    public void Dispose() { }
}
