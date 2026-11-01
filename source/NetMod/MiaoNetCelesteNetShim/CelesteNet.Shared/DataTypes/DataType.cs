using System.Diagnostics.CodeAnalysis;

namespace Celeste.Mod.CelesteNet.DataTypes;

public abstract class DataType
{
    public virtual DataFlags DataFlags => DataFlags.None;
    public virtual MetaType[] Meta { get; set; } = [];
    public virtual bool FilterHandle(DataContext context) => true;
    public virtual bool FilterSend(DataContext context) => true;
    public virtual MetaType[] GenerateMeta(DataContext context) => Meta;
    public virtual void FixupMeta(DataContext context) { }
    public virtual MetaUpdateContext UpdateMeta(DataContext context) => new(context, this);

    // Meta is part of the payload: mods rely on FixupMeta resolving the sender/player
    // references before their own Read runs.
    public virtual void ReadAll(CelesteNetBinaryReader reader)
    {
        Meta = ReadMeta(reader);
        FixupMeta(reader.Data);
        Read(reader);
    }

    public virtual void WriteAll(CelesteNetBinaryWriter writer)
    {
        Meta = GenerateMeta(writer.Data);
        WriteMeta(writer, Meta);
        Write(writer);
    }

    protected virtual MetaType[] ReadMeta(CelesteNetBinaryReader reader)
    {
        MetaType[] meta = new MetaType[reader.ReadByte()];
        for (int i = 0; i < meta.Length; i++)
            meta[i] = reader.Data.ReadMeta(reader);
        return meta;
    }

    protected virtual void WriteMeta(CelesteNetBinaryWriter writer, MetaType[] meta)
    {
        writer.Write((byte)meta.Length);
        foreach (MetaType single in meta)
            writer.Data.WriteMeta(writer, single);
    }

    protected virtual void Read(CelesteNetBinaryReader reader) { }
    protected virtual void Write(CelesteNetBinaryWriter writer) { }
    public virtual string GetTypeID(DataContext context) => context.DataTypeToID.GetValueOrDefault(GetType(), "");
    public virtual string GetSource(DataContext context) => context.DataTypeToSource.GetValueOrDefault(GetType(), "");

    public virtual bool Is<T>(DataContext context) where T : MetaType<T>
        => Meta.OfType<T>().Any();

    public virtual T Get<T>(DataContext context) where T : MetaType<T>
        => Meta.OfType<T>().FirstOrDefault()
           ?? throw new InvalidDataException($"DataType {GetTypeID(context)} doesn't have MetaType {typeof(T).Name}.");

    public virtual T? GetOpt<T>(DataContext context) where T : MetaType<T>
        => Meta.OfType<T>().FirstOrDefault();

    public virtual bool TryGet<T>(DataContext context, [NotNullWhen(true)] out T? value) where T : MetaType<T>
    {
        value = Meta.OfType<T>().FirstOrDefault();
        return value is not null;
    }
}

public abstract class DataType<T> : DataType where T : DataType<T>
{
    public static string DataID = typeof(T).Name;
    public static string DataSource = typeof(T).Assembly.GetName().Name ?? DataID;
    public T ReadT(CelesteNetBinaryReader reader) { Read(reader); return (T)this; }
    public T ReadAllT(CelesteNetBinaryReader reader) { ReadAll(reader); return (T)this; }
    public override string GetTypeID(DataContext context) => DataID;
    public override string GetSource(DataContext context) => DataSource;
}

/// <summary>A data type which is not registered on this side.</summary>
public class DataUnparsed : DataType<DataUnparsed> { }
