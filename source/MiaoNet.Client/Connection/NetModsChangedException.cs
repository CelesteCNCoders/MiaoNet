namespace Celeste.Mod.MiaoNet;

// TODO reasons of disconnection
public sealed class NetModsChangedException : Exception
{
    public NetModsChangedException()
        : base(SR.NetModsChangedException)
    {
    }
}
