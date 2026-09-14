using System.Collections.Generic;
using Celeste.Editor;
using Celeste.Mod.MiaoNet.UI.DebugMap;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

// draws every self-channel player over the level editor's map.
// the camera transform stays here, on the XNA side: world -> screen is an adapter concern, and
// the overlay node only gets finished screen positions.
public sealed class DebugMapComponent : MiaoNetComponent
{
    private readonly UIRoot ui = new();
    private readonly MiaoNetUICanvas canvas = new();
    private readonly DebugMapOverlayNode overlay;
    private readonly List<DebugMapMarker> markers = [];

    public DebugMapComponent(MiaoNetContext context)
        : base(context)
    {
        overlay = new DebugMapOverlayNode
        {
            Style = new UIStyle { TextRenderer = MiaoNetTextRenderer.Instance },
        };

        ui.SetRoot(overlay);
    }

    public override void Render()
    {
        if (Engine.Scene is not MapEditor)
        {
            return;
        }

        markers.Clear();
        foreach (OnlinePlayer player in ClientState.SelfChannel.Players)
        {
            if (player.State is null)
            {
                continue;
            }

            Vector2 rPos = player.State.Position;
            Vector2 pos = new(rPos.X / 8f + 0.5f, rPos.Y / 8f + 0.5f);
            pos -= MapEditor.Camera.Position;
            pos = pos.Round();
            pos *= MapEditor.Camera.Zoom;
            pos += new Vector2(Celeste.TargetWidth, Celeste.TargetHeight) / 2f;

            PlayerGraphicsInfo gfx = player.GraphicsInfo ?? PlayerGraphicsInfo.Default;
            Color hair = gfx.GetHairInfo(player.State.Dashes).Color;
            markers.Add(new DebugMapMarker(
                player.Info.Name,
                new UIOffset(pos.X, pos.Y),
                UIColor.FromBytes(hair.R, hair.G, hair.B, hair.A)));
        }

        overlay.Markers = markers;

        ui.Layout(Engine.Width, Engine.Height);
        canvas.BeginFrame();
        try
        {
            ui.Paint(canvas);
        }
        finally
        {
            canvas.EndFrame();
        }
    }
}
