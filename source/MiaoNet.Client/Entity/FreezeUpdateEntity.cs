namespace Celeste.Mod.MiaoNet;

[Tracked(inherited: true)]
public abstract class FreezeUpdateEntity : Entity
{
    protected FreezeUpdateEntity()
    {
    }

    protected FreezeUpdateEntity(Vector2 position) : base(position)
    {
    }
}
