namespace MiaoNet.Server.GameScope;

public sealed class GlobalScope: Scope<NoKey, int>
{
    public override bool Permanent => true;

    public GlobalScope(): base(NoKey.Instance, null)
    {
    }

    public override string ToString() => "Global";
}