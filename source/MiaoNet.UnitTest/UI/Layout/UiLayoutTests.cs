using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Layout;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace MiaoNet.UnitTest;

// the retained layout core: constraints, flex distribution, padding, alignment, stacking and flex
// spacers, with no Celeste / XNA dependency.
[TestClass]
public sealed class UiLayoutTests
{
    private static void AssertClose(float expected, float actual, string message)
        => Assert.IsLessThan(
            1e-4f,
            MathF.Abs(expected - actual),
            $"{message}: expected {expected}, got {actual}");

    private static void AssertRect(UIRect actual, float x, float y, float width, float height, string message)
    {
        AssertClose(x, actual.X, $"{message}.X");
        AssertClose(y, actual.Y, $"{message}.Y");
        AssertClose(width, actual.Width, $"{message}.Width");
        AssertClose(height, actual.Height, $"{message}.Height");
    }

    // a leaf with a fixed desired size that records where it was arranged
    private sealed class Probe(float width, float height) : UINode
    {
        public float Width { get; set; } = width;

        public float Height { get; set; } = height;

        public UIRect? LastBounds { get; private set; }

        protected override UISize OnMeasure(BoxConstraints constraints)
            => constraints.Constrain(new UISize(Width, Height));

        protected override void OnArrange(UIRect bounds) => LastBounds = bounds;
    }

    // ---------------------------------------------------------------- constraints

    [TestMethod]
    public void BoxConstraints_ConstrainsAndDeflates()
    {
        var constraints = new BoxConstraints(0f, 100f, 0f, 50f);

        Assert.AreEqual(new UISize(80f, 40f), constraints.Constrain(new UISize(80f, 40f)));
        Assert.AreEqual(new UISize(100f, 50f), constraints.Constrain(new UISize(500f, 500f)));
        Assert.AreEqual(new UISize(0f, 0f), constraints.Constrain(new UISize(-5f, -5f)));

        BoxConstraints deflated = constraints.Deflate(new EdgeInsets(10f, 5f, 20f, 15f));
        AssertClose(0f, deflated.MinWidth, "deflated.MinWidth");
        AssertClose(70f, deflated.MaxWidth, "deflated.MaxWidth");
        AssertClose(0f, deflated.MinHeight, "deflated.MinHeight");
        AssertClose(30f, deflated.MaxHeight, "deflated.MaxHeight");
    }

    [TestMethod]
    public void BoxConstraints_LoosenDropsMinimumsButKeepsMaximums()
    {
        BoxConstraints loosened = BoxConstraints.Tight(120f, 60f).Loosen();

        AssertClose(0f, loosened.MinWidth, "loosened.MinWidth");
        AssertClose(120f, loosened.MaxWidth, "loosened.MaxWidth");
        Assert.AreEqual(new UISize(20f, 10f), loosened.Constrain(new UISize(20f, 10f)));
    }

    [TestMethod]
    public void BoxConstraints_TightenWidthKeepsRangeOrdered()
    {
        BoxConstraints tightened = new BoxConstraints(10f, 200f, 10f, 200f).TightenWidth(50f);

        AssertClose(50f, tightened.MinWidth, "tightened.MinWidth");
        AssertClose(50f, tightened.MaxWidth, "tightened.MaxWidth");
    }

    [TestMethod]
    public void UiRect_SnapFloorsOriginAndDerivesExtentFromFarEdge()
    {
        var rect = new UIRect(10.4f, 20.6f, 30.3f, 40.2f);
        UIRect snapped = rect.Snap();

        // x: floor(10.4)=10; width: floor(10.4+30.3)-10 = 40-10 = 30
        // y: floor(20.6)=20; height: floor(20.6+40.2)-20 = 60-20 = 40
        AssertRect(snapped, 10f, 20f, 30f, 40f, "snapped");
    }

    [TestMethod]
    public void UiScale_MapsSettingsOntoTheDocumentedExponentialRamp()
    {
        // A1..A5 from the parameter spec
        AssertClose(0.25f, UIScale.FromSetting(1), "A1 setting 1");
        AssertClose(0.8f, UIScale.FromSetting(20), "A2 setting 20");
        AssertClose(0.33953f, UIScale.FromSetting(6), "A3 setting 6");
        AssertClose(0.43373f, UIScale.FromSetting(10), "A4 setting 10");
        AssertClose(0.25f, UIScale.FromSetting(0), "A5 clamps below the range");
        AssertClose(0.8f, UIScale.FromSetting(25), "A5 clamps above the range");
    }

    // ---------------------------------------------------------------- flex

    [TestMethod]
    public void Flex_HorizontalStart_PlacesChildrenInOrderWithSpacing()
    {
        var flex = new FlexNode
        {
            Axis = FlexAxis.Horizontal,
            Spacing = 10f,
            MainAxisAlignment = MainAxisAlignment.Start,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
        };
        var first = new Probe(30f, 10f);
        var second = new Probe(50f, 10f);
        flex.Add(first);
        flex.Add(second);

        var constraints = BoxConstraints.Tight(200f, 50f);
        Assert.AreEqual(new UISize(200f, 50f), flex.Measure(constraints));
        flex.Arrange(new UIRect(0f, 0f, 200f, 50f));

        // Stretch gives children a tight cross axis, so their height becomes 50.
        AssertRect(first.LastBounds!.Value, 0f, 0f, 30f, 50f, "first");
        AssertRect(second.LastBounds!.Value, 40f, 0f, 50f, 50f, "second");
    }

    [TestMethod]
    public void Flex_HorizontalEnd_PushesContentToTheFarEdge()
    {
        FlexNode flex = CreateTwoChildRow(MainAxisAlignment.End);
        flex.Arrange(new UIRect(0f, 0f, 200f, 50f));

        // content = 30 + 50 + 10 spacing = 90, leftover = 110
        AssertClose(110f, flex.Children[0].Bounds.X, "first.X");
        AssertClose(150f, flex.Children[1].Bounds.X, "second.X");
    }

    [TestMethod]
    public void Flex_HorizontalCenter_SplitsLeftoverEvenly()
    {
        FlexNode flex = CreateTwoChildRow(MainAxisAlignment.Center);
        flex.Arrange(new UIRect(0f, 0f, 200f, 50f));

        AssertClose(55f, flex.Children[0].Bounds.X, "first.X");
        AssertClose(95f, flex.Children[1].Bounds.X, "second.X");
    }

    [TestMethod]
    public void Flex_HorizontalSpaceBetween_AddsLeftoverToTheGap()
    {
        FlexNode flex = CreateTwoChildRow(MainAxisAlignment.SpaceBetween);
        flex.Arrange(new UIRect(0f, 0f, 200f, 50f));

        // step = spacing 10 + leftover 110 = 120
        AssertClose(0f, flex.Children[0].Bounds.X, "first.X");
        AssertClose(150f, flex.Children[1].Bounds.X, "second.X");
    }

    [TestMethod]
    public void Flex_Vertical_PlacesChildrenTopDownAndStretchesWidth()
    {
        var flex = new FlexNode
        {
            Axis = FlexAxis.Vertical,
            Spacing = 10f,
            MainAxisAlignment = MainAxisAlignment.Start,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
        };
        var first = new Probe(10f, 30f);
        var second = new Probe(10f, 50f);
        flex.Add(first);
        flex.Add(second);

        Assert.AreEqual(new UISize(100f, 200f), flex.Measure(BoxConstraints.Tight(100f, 200f)));
        flex.Arrange(new UIRect(0f, 0f, 100f, 200f));

        AssertRect(first.LastBounds!.Value, 0f, 0f, 100f, 30f, "first");
        AssertRect(second.LastBounds!.Value, 0f, 40f, 100f, 50f, "second");
    }

    [TestMethod]
    public void Flex_CrossAxisAlignmentStart_LeavesChildAtItsOwnCrossSize()
    {
        var flex = new FlexNode
        {
            Axis = FlexAxis.Horizontal,
            CrossAxisAlignment = CrossAxisAlignment.Start,
        };
        var child = new Probe(30f, 10f);
        flex.Add(child);

        flex.Measure(BoxConstraints.Tight(200f, 50f));
        flex.Arrange(new UIRect(0f, 0f, 200f, 50f));

        AssertRect(child.LastBounds!.Value, 0f, 0f, 30f, 10f, "child");
    }

    [TestMethod]
    public void Flex_SpacerAbsorbsRemainingMainAxisSpace()
    {
        var flex = new FlexNode
        {
            Axis = FlexAxis.Horizontal,
            CrossAxisAlignment = CrossAxisAlignment.Start,
        };
        var left = new Probe(30f, 10f);
        var spacer = new SpacerNode();
        var right = new Probe(30f, 10f);
        flex.Add(left);
        flex.Add(spacer);
        flex.Add(right);

        flex.Measure(BoxConstraints.Tight(200f, 20f));
        flex.Arrange(new UIRect(0f, 0f, 200f, 20f));

        AssertRect(left.LastBounds!.Value, 0f, 0f, 30f, 10f, "left");
        AssertRect(spacer.Bounds, 30f, 0f, 140f, 0f, "spacer");
        AssertRect(right.LastBounds!.Value, 170f, 0f, 30f, 10f, "right");
    }

    private static FlexNode CreateTwoChildRow(MainAxisAlignment alignment)
    {
        var flex = new FlexNode
        {
            Axis = FlexAxis.Horizontal,
            Spacing = 10f,
            MainAxisAlignment = alignment,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
        };
        flex.Add(new Probe(30f, 10f));
        flex.Add(new Probe(50f, 10f));
        flex.Measure(BoxConstraints.Tight(200f, 50f));
        return flex;
    }

    [TestMethod]
    public void Flex_HidingAChildReflowsTheRemainingOnes()
    {
        var flex = new FlexNode
        {
            Axis = FlexAxis.Horizontal,
            CrossAxisAlignment = CrossAxisAlignment.Start,
        };
        var first = new Probe(30f, 10f);
        var second = new Probe(30f, 10f);
        flex.Add(first);
        flex.Add(second);

        flex.Measure(BoxConstraints.Tight(200f, 20f));
        flex.Arrange(new UIRect(0f, 0f, 200f, 20f));
        AssertClose(30f, second.Bounds.X, "second.X before hiding the first");

        // visibility participates in layout, so this has to invalidate the flex's cached measure
        first.IsVisible = false;
        flex.Measure(BoxConstraints.Tight(200f, 20f));
        flex.Arrange(new UIRect(0f, 0f, 200f, 20f));

        AssertClose(0f, second.Bounds.X, "second.X after hiding the first");
    }

    // ---------------------------------------------------------------- box / align / stack

    [TestMethod]
    public void BoxNode_AddsPaddingAroundItsChild()
    {
        var box = new BoxNode
        {
            Style = new UIStyle { Padding = new EdgeInsets(8f) },
        };
        var child = new Probe(30f, 20f);
        box.Child = child;

        UISize size = box.Measure(BoxConstraints.Loose(200f, 200f));
        Assert.AreEqual(new UISize(46f, 36f), size);

        box.Arrange(new UIRect(10f, 10f, 46f, 36f));
        AssertRect(child.LastBounds!.Value, 18f, 18f, 30f, 20f, "child");
    }

    [TestMethod]
    public void AlignNode_CenterPlacesChildInTheMiddle()
    {
        var align = new AlignNode
        {
            Alignment = UIAlignment.Center,
            Style = new UIStyle { Width = 100f, Height = 100f },
        };
        var child = new Probe(20f, 10f);
        align.Child = child;

        align.Measure(BoxConstraints.Tight(100f, 100f));
        align.Arrange(new UIRect(0f, 0f, 100f, 100f));

        AssertRect(child.LastBounds!.Value, 40f, 45f, 20f, 10f, "child");
    }

    [TestMethod]
    public void AlignNode_BottomRightPlacesChildAtTheFarCorner()
    {
        var align = new AlignNode
        {
            Alignment = UIAlignment.BottomRight,
            Style = new UIStyle { Width = 100f, Height = 100f },
        };
        var child = new Probe(20f, 10f);
        align.Child = child;

        align.Measure(BoxConstraints.Tight(100f, 100f));
        align.Arrange(new UIRect(0f, 0f, 100f, 100f));

        AssertRect(child.LastBounds!.Value, 80f, 90f, 20f, 10f, "child");
    }

    [TestMethod]
    public void StackNode_IsAsLargeAsItsLargestChildAndAlignsEachChild()
    {
        var stack = new StackNode
        {
            Alignment = UIAlignment.Center,
            Style = new UIStyle { Width = 100f, Height = 100f },
        };
        var big = new Probe(40f, 20f);
        var small = new Probe(10f, 10f);
        stack.Add(big);
        stack.Add(small);

        Assert.AreEqual(new UISize(100f, 100f), stack.Measure(BoxConstraints.Tight(100f, 100f)));
        stack.Arrange(new UIRect(0f, 0f, 100f, 100f));

        AssertRect(big.LastBounds!.Value, 30f, 40f, 40f, 20f, "big");
        AssertRect(small.LastBounds!.Value, 45f, 45f, 10f, 10f, "small");
    }

    // ---------------------------------------------------------------- root / invalidation

    [TestMethod]
    public void UiRoot_LayoutMeasuresAndArrangesTheWholeTree()
    {
        var root = new FlexNode
        {
            Axis = FlexAxis.Vertical,
            Style = new UIStyle { Width = 320f, Height = 180f },
        };
        var child = new Probe(100f, 50f);
        root.Add(child);

        var ui = new UIRoot();
        ui.SetRoot(root);
        ui.Layout(320f, 180f);

        Assert.AreEqual(new UISize(320f, 180f), ui.Size);
        AssertRect(root.Bounds, 0f, 0f, 320f, 180f, "root");
        AssertRect(child.LastBounds!.Value, 0f, 0f, 320f, 50f, "child");
    }

    [TestMethod]
    public void UiRoot_HitTestReturnsTheDeepestVisibleNode()
    {
        var root = new BoxNode
        {
            Style = new UIStyle
            {
                Width = 100f,
                Height = 100f,
                Padding = new EdgeInsets(10f),
            },
        };
        var child = new Probe(80f, 80f);
        root.Child = child;

        var ui = new UIRoot();
        ui.SetRoot(root);
        ui.Layout(100f, 100f);

        Assert.AreSame(child, ui.HitTest(new UIOffset(50f, 50f)));

        // The padding ring belongs to the box itself, so it still hits the box.
        Assert.AreSame(root, ui.HitTest(new UIOffset(5f, 5f)));

        // Outside the root rectangle nothing is hit.
        Assert.IsNull(ui.HitTest(new UIOffset(150f, 150f)));
    }

    [TestMethod]
    public void InvalidateMeasure_PropagatesAndPicksUpNewSizes()
    {
        var root = new FlexNode
        {
            Axis = FlexAxis.Horizontal,
            CrossAxisAlignment = CrossAxisAlignment.Start,
            Style = new UIStyle { Width = 200f, Height = 50f },
        };
        var first = new Probe(30f, 10f);
        var second = new Probe(30f, 10f);
        root.Add(first);
        root.Add(second);

        var ui = new UIRoot();
        ui.SetRoot(root);
        ui.Layout(200f, 50f);
        AssertClose(30f, second.Bounds.X, "second.X before");

        first.Width = 80f;
        first.InvalidateMeasure();
        ui.Layout(200f, 50f);

        AssertClose(80f, second.Bounds.X, "second.X after");
    }
}
