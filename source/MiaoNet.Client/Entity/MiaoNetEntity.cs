namespace Celeste.Mod.MiaoNet;

[Tracked(inherited: true)]
public abstract class MiaoNetEntity : Entity
{
    protected MiaoNetEntity()
    {
        SpeedrunToolInterop.IgnoreSaveState?.Invoke(this, true);
    }

    protected MiaoNetEntity(Vector2 position) : base(position)
    {
        SpeedrunToolInterop.IgnoreSaveState?.Invoke(this, true);
    }
}
