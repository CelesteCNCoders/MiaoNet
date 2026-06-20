namespace Celeste.Mod.MiaoNet;

public static class MiaoNetTag
{
    private static readonly int Base = Tags.Persistent | Tags.TransitionUpdate | Tags.FrozenUpdate | Tags.PauseUpdate | Tags.Global;

    public static readonly int Normal = Base;

    public static readonly int Hud = Base | TagsExt.SubHUD;
}
