namespace Celeste.Mod.MiaoNet;

[Tracked(inherited: true)]
public abstract class MiaoNetEntity : Entity
{
    protected MiaoNetEntity()
    {
    }

    protected MiaoNetEntity(Vector2 position) : base(position)
    {
    }
}
