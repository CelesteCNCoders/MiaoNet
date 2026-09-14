using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Status;

namespace MiaoNet.UnitTest;

// geometry of the connection status overlay: corner placement and the shared bottom line
[TestClass]
public sealed class StatusPanelUiTests
{
    private static void AssertClose(float expected, float actual, string message)
        => Assert.IsLessThan(1e-4f, MathF.Abs(expected - actual), $"{message}: expected {expected}, got {actual}");

    private static void AssertRect(UiRect actual, float x, float y, float width, float height, string message)
    {
        AssertClose(x, actual.X, $"{message}.X");
        AssertClose(y, actual.Y, $"{message}.Y");
        AssertClose(width, actual.Width, $"{message}.Width");
        AssertClose(height, actual.Height, $"{message}.Height");
    }

    private sealed class Probe(float width, float height) : UiNode
    {
        protected override UiSize OnMeasure(BoxConstraints constraints)
            => constraints.Constrain(new UiSize(width, height));
    }

    [TestMethod]
    public void StatusPanel_PutsTheCogwheelInTheCornerAndTheMessageBesideIt()
    {
        var icon = new Probe(40f, 30f);
        var message = new Probe(100f, 12f);
        var panel = new StatusPanelNode(icon, message);

        var ui = new UiRoot();
        ui.SetRoot(panel);
        ui.Layout(800f, 600f);

        // CornerOffset from the left, and the same from the bottom.
        AssertRect(icon.Bounds, 64f, 600f - 64f - 30f, 40f, 30f, "icon");

        // The message's bottom lines up with the cogwheel's, MessageGap to its right.
        AssertRect(message.Bounds, 64f + 40f + 32f, 600f - 64f - 12f, 100f, 12f, "message");
        AssertClose(icon.Bounds.Bottom, message.Bounds.Bottom, "shared bottom line");
    }

    [TestMethod]
    public void StatusPanel_ConstantsMatchTheOriginalGeometry()
    {
        AssertClose(64f, StatusPanelNode.CornerOffset, "corner offset");
        AssertClose(32f, StatusPanelNode.MessageGap, "message gap");
    }

    [TestMethod]
    public void StatusPanel_FollowsAScreenSizeChange()
    {
        var icon = new Probe(40f, 30f);
        var message = new Probe(100f, 12f);
        var panel = new StatusPanelNode(icon, message);

        var ui = new UiRoot();
        ui.SetRoot(panel);
        ui.Layout(800f, 600f);
        float firstBottom = icon.Bounds.Bottom;

        ui.Layout(800f, 400f);

        AssertClose(400f - 64f, icon.Bounds.Bottom, "the overlay tracks the screen height");
        Assert.AreNotEqual(firstBottom, icon.Bounds.Bottom, "and it actually moved");
    }
}
