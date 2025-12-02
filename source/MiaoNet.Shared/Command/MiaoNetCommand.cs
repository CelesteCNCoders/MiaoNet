namespace MiaoNet.Shared;

public abstract partial class MiaoNetCommand
{
    public record SegmentDescription(string Name, string? Description, CommandSegmentType Type);

    public int ID { get; set; }

    public abstract string Name { get; }

    public virtual string? Description => null;

    public virtual IReadOnlyCollection<string>? Aliases => null;

    public abstract IReadOnlyCollection<SegmentDescription> Segments { get; }

#if MIAO_SERVER
    //public abstract void 
#elif MIAO_CLIENT

#endif
}