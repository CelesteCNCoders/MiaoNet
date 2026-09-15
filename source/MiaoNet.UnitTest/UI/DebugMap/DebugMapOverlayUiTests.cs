using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.DebugMap;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace MiaoNet.UnitTest;

// the debug-map overlay: coordinates come in screen-relative from the component, and the node's
// only job is to draw them in order, name first and marker square on top.
[TestClass]
public sealed class DebugMapOverlayUiTests
{
    private static void AssertClose(float expected, float actual, string message)
        => Assert.IsLessThan(1e-4f, MathF.Abs(expected - actual), $"{message}: expected {expected}, got {actual}");

    // records draw order across both the text backend and the canvas
    private sealed class Recorder(List<string> order) : ITextRenderer
    {
        private readonly List<string> order = order;

        public List<(string Text, UIOffset Position, TextStyle Style)> Draws { get; } = [];

        public UISize Measure(string text, TextStyle style) => new(text.Length * 10f, 12f);

        public void Draw(IUICanvas canvas, string text, UIOffset position, TextStyle style)
        {
            order.Add($"text:{text}");
            Draws.Add((text, position, style));
        }

        public bool CanRender(int character, TextStyle style) => true;
    }

    private sealed class Canvas(List<string> order) : IUICanvas
    {
        private readonly List<string> order = order;

        public List<(UIRect Rect, UIColor Color)> Fills { get; } = [];

        public void FillRect(UIRect rect, UIColor color)
        {
            order.Add("rect");
            Fills.Add((rect, color));
        }

        public void DrawLine(UIOffset from, UIOffset to, UIColor color, float thickness)
        {
        }

        public void PushClip(UIRect rect)
        {
        }

        public void PopClip()
        {
        }
    }

    [TestMethod]
    public void Overlay_DrawsTheNameFirstAndTheMarkerSquareOnTop()
    {
        var order = new List<string>();
        var text = new Recorder(order);
        var canvas = new Canvas(order);
        var overlay = new DebugMapOverlayNode
        {
            Style = new UIStyle { TextRenderer = text },
            Markers = [new DebugMapMarker("Alice", new UIOffset(100f, 50f), UIColor.FromBytes(1, 2, 3, 255))],
        };

        overlay.PaintTree(canvas, 1f);

        // shared order list: the name goes down before the square that covers it
        CollectionAssert.AreEqual(new[] { "text:Alice", "rect" }, order.ToArray());
    }

    [TestMethod]
    public void Overlay_UsesTheOriginalTextAnchorScaleAndOutline()
    {
        var order = new List<string>();
        var text = new Recorder(order);
        var canvas = new Canvas(order);
        var overlay = new DebugMapOverlayNode
        {
            Style = new UIStyle { TextRenderer = text },
            Markers = [new DebugMapMarker("Bob", new UIOffset(100f, 50f), UIColor.White)],
        };

        overlay.PaintTree(canvas, 1f);

        (string drawn, UIOffset position, TextStyle style) = text.Draws[0];
        Assert.AreEqual("Bob", drawn);
        AssertClose(0.5f, style.Scale, "half scale");
        Assert.AreEqual(HorizontalAnchor.Center, style.HorizontalAnchor, "centred horizontally");
        Assert.AreEqual(VerticalAnchor.Bottom, style.VerticalAnchor, "bottom anchored");
        Assert.IsTrue(style.Decorations.HasFlag(TextDecoration.Outline), "outlined");

        // Drawn one pixel above the marker.
        AssertClose(100f, position.X, "text X");
        AssertClose(49f, position.Y, "text Y");
    }

    [TestMethod]
    public void Overlay_MarkerSquareIsEightPixelsCentredOnThePosition()
    {
        var canvas = new Canvas([]);
        UIColor hair = UIColor.FromBytes(10, 20, 30, 255);
        var overlay = new DebugMapOverlayNode
        {
            Style = new UIStyle { TextRenderer = new Recorder([]) },
            Markers = [new DebugMapMarker("C", new UIOffset(100f, 50f), hair)],
        };

        overlay.PaintTree(canvas, 1f);

        (UIRect rect, UIColor color) = canvas.Fills[0];
        AssertClose(96f, rect.X, "rect X");
        AssertClose(46f, rect.Y, "rect Y");
        AssertClose(8f, rect.Width, "rect width");
        AssertClose(8f, rect.Height, "rect height");
        Assert.AreEqual(hair, color, "hair colour");
    }

    [TestMethod]
    public void Overlay_WithNoMarkersDrawsNothing()
    {
        var canvas = new Canvas([]);
        var overlay = new DebugMapOverlayNode { Style = new UIStyle { TextRenderer = new Recorder([]) } };

        overlay.PaintTree(canvas, 1f);

        Assert.HasCount(0, canvas.Fills);
    }
}
