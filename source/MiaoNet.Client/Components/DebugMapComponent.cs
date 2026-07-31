using Celeste.Editor;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class DebugMapComponent : MiaoNetComponent
{
    public DebugMapComponent(MiaoNetContext context)
        : base(context)
    {
    }

    public override void Render()
    {
        if (Engine.Scene is not MapEditor)
            return;

        foreach (var player in ClientState.SelfChannel.Players)
        {
            if (player.State is null)
                continue;

            Vector2 rPos = player.State!.Position;
            Vector2 pos = new Vector2(rPos.X / 8f + 0.5f, rPos.Y / 8f + 0.5f);
            pos -= MapEditor.Camera.Position;
            pos = pos.Round();
            pos *= MapEditor.Camera.Zoom;
            pos += new Vector2(Celeste.TargetWidth, Celeste.TargetHeight) / 2f;

            MiaoNetFont.DrawOutline(player.Info.Name, pos - Vector2.UnitY, new Vector2(0.5f, 1.0f), Vector2.One / 2f, Color.White);
            var gfx = player.GraphicsInfo ?? PlayerGraphicsInfo.Default;
            Draw.Rect(pos - Vector2.One * 4f, 8f, 8f, gfx.GetHairInfo(player.State.Dashes).Color);
        }
    }
}
