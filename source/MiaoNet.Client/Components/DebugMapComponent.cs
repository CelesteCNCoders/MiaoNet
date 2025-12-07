using Celeste.Editor;

namespace Celeste.Mod.MiaoNet;

public sealed class DebugMapComponent : MiaoNetComponent
{
    public DebugMapComponent(MiaoNetContext context)
        : base(context)
    {
    }

    public override void Update()
    {
        if (Engine.Scene is not MapEditor debugRoom)
            return;
    }

    public override void Render()
    {
        if (Engine.Scene is not MapEditor debugRoom)
            return;
        Draw.Rect(0f, 0f, 100f, 100f, Color.Red);
    }
}
