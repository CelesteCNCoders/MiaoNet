using System.Collections.Generic;

namespace Celeste.Mod.MiaoNet.UI.Input;

// one frame of physical input, already translated into named actions by the adapter. reused across
// frames, so Clear() it at the start of each poll.
public sealed class UIInputFrame
{
    private readonly HashSet<UIInputAction> pressed = [];
    private readonly HashSet<UIInputAction> held = [];

    // edges that fired this frame, including virtual-button repeats.
    public IReadOnlyCollection<UIInputAction> Pressed => pressed;

    // actions whose input is currently held down.
    public IReadOnlyCollection<UIInputAction> Held => held;

    // wheel scroll for the chat list, in pixel units.
    public float ChatScrollDelta { get; set; }

    public void Clear()
    {
        pressed.Clear();
        held.Clear();
        ChatScrollDelta = 0f;
    }

    public void Press(UIInputAction action) => pressed.Add(action);

    public void Hold(UIInputAction action) => held.Add(action);
}
