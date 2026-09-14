namespace Celeste.Mod.MiaoNet.UI.Layout;

// main axis of a FlexNode
public enum FlexAxis
{
    Horizontal,
    Vertical,
}

// how children spread along the main axis
public enum MainAxisAlignment
{
    Start,
    Center,
    End,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
}

// how children sit along the cross axis
public enum CrossAxisAlignment
{
    Start,
    Center,
    End,

    // children get a tight cross-axis constraint and fill the container
    Stretch,
}
