namespace Celeste.Mod.MiaoNet;

// will be updated even in freeze frames
// and ignored by save states of SpeedrunTool
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
