using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.DebugMap;

// one player marker in the debug-map overlay, already in screen coordinates.
public readonly record struct DebugMapMarker(string Name, UIOffset Position, UIColor HairColor);

// per-player markers drawn over the level editor's map: a small hair-coloured square with the
// player's name outlined above it.
//
// marker positions come from the editor camera, so they move every frame. we paint at absolute
// screen coordinates instead of laying out one child per marker: building and arranging children
// every frame would be pure churn for no layout benefit, and the marker count is bounded by the
// players in the channel.
public sealed class DebugMapOverlayNode : UINode
{
    // half the edge of the hair-coloured marker square.
    public const float MarkerRadius = 4f;

    // the name is drawn at half scale.
    public const float TextScale = 0.5f;

    // lift applied to the name so it clears the marker.
    public const float TextLift = 1f;

    private IReadOnlyList<DebugMapMarker> markers = [];

    // replaced wholesale every frame.
    public IReadOnlyList<DebugMapMarker> Markers
    {
        get => markers;
        set => markers = value ?? [];
    }

    protected override UISize OnMeasure(BoxConstraints constraints)
    {
        float width = float.IsInfinity(constraints.MaxWidth) ? 0f : constraints.MaxWidth;
        float height = float.IsInfinity(constraints.MaxHeight) ? 0f : constraints.MaxHeight;
        return constraints.Constrain(new UISize(width, height));
    }

    protected override void PaintSelf(IUICanvas canvas, float opacity)
    {
        if (markers.Count == 0)
        {
            return;
        }

        ITextRenderer? text = Style.TextRenderer;
        var textStyle = new TextStyle
        {
            Scale = TextScale,
            LineHeight = Style.LineHeight,
            Color = MiaoNetUITheme.DebugMap.Name * opacity,
            HorizontalAnchor = HorizontalAnchor.Center,
            VerticalAnchor = VerticalAnchor.Bottom,
            Decorations = TextDecoration.Outline,
        };

        foreach (DebugMapMarker marker in markers)
        {
            // name first, marker square on top of it.
            text?.Draw(
                canvas,
                marker.Name,
                new UIOffset(marker.Position.X, marker.Position.Y - TextLift),
                textStyle);

            canvas.FillRect(
                new UIRect(
                    marker.Position.X - MarkerRadius,
                    marker.Position.Y - MarkerRadius,
                    MarkerRadius * 2f,
                    MarkerRadius * 2f),
                marker.HairColor * opacity);
        }
    }
}
