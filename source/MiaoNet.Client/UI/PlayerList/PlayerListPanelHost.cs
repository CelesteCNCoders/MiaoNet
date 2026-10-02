using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Input;
using Celeste.Mod.MiaoNet.UI.Layout;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;

namespace Celeste.Mod.MiaoNet.UI.PlayerList;

// everything the player list needs from outside the tree: the settings, the engine sizes, the
// router and the data component. the tree itself is PlayerListPanelNode, and the decisions taken
// here are PlayerListController's, which is the half that has tests.
//
// unlike the rest of ui/ this file is client-only (the unit test project doesn't compile it): it
// reads MiaoNetModule and Engine, the same way the concrete renderers do.
public sealed class PlayerListPanelHost
{
    private readonly MiaoNetContext context;
    private readonly PlayerListComponent data;
    private readonly PlayerListController controller = new();
    private readonly PlayerListPanelNode panel;
    private readonly AlignNode host;
    private readonly UIInputHandle input;

    // what the panel was last built from; null means "not built yet". one value instead of three
    // fields, so there is no way to reset two of them and forget the third.
    private readonly record struct BuiltContent(int Version, float Scale, bool LiveMode);

    private BuiltContent? built;

    // set by the toggle reaction, consumed by Update
    private bool toggleRequested;

    public PlayerListPanelHost(MiaoNetContext context, ITextRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(renderer);

        this.context = context;
        data = context.PlayerListComponent;

        panel = new PlayerListPanelNode(renderer);
        host = new AlignNode
        {
            // an align node fills the box the layout offers -- the screen here -- so the panel can
            // anchor to the screen corner without anyone telling the host how big the screen is
            Alignment = UIAlignment.TopLeft,
            Child = panel,
        };

        // press mode needs the edge, hold mode the level; either way the router only delivers the
        // action while the list is allowed to act on it.
        input = context.UIInput.Handle(UIInputScope.PlayerList);
        input.On(UIInputAction.PlayerListToggle, RecordToggleRequest);
    }

    public UINode Root => host;

    public PlayerListController Controller => controller;

    public void Update()
    {
        var settings = MiaoNetModule.Settings;

        bool openedOrClosed = controller.UpdateOpenState(
            settings.PlayerListButtonMode == ButtonMode.Press,
            toggleRequested,
            input.IsHeld(UIInputAction.PlayerListToggle),
            context.IsSuitableToOpenUI);
        toggleRequested = false;

        if (openedOrClosed)
        {
            data.Active = controller.IsOpen;
            context.UIInput.SetFocus(controller.IsOpen ? UIInputScope.PlayerList : UIInputScope.Neutral);
        }

        // the routing table already restricts these to player-list focus, i.e. while it's open
        controller.ScrollUpHeld = input.IsHeld(UIInputAction.PlayerListScrollUp);
        controller.ScrollDownHeld = input.IsHeld(UIInputAction.PlayerListScrollDown);
        controller.Update(Engine.RawDeltaTime);

        host.IsVisible = controller.IsOpen;
        if (!controller.IsOpen)
        {
            return;
        }

        RebuildIfNeeded(settings.PlayerListUIScaleValue, settings.LiveMode);
        panel.SetPausedIconOffset(controller.PausedIconOffset);
        panel.Offset = controller.Scroll;
    }

    // content height is only known once the panel has been measured, and clamping can move the
    // offset. re-arranging (no second measure) is what puts a freshly clamped list on the right
    // rows this frame instead of next frame.
    public void AfterLayout(UIRoot ui)
    {
        ArgumentNullException.ThrowIfNull(ui);

        if (!controller.IsOpen)
        {
            return;
        }

        // both come out of the layout pass that just ran, so the screen size never has to be pushed
        // in from outside
        controller.ViewportHeight = panel.Bounds.Height;
        controller.ContentHeight = panel.ContentSize.Height;
        float before = controller.Scroll;
        controller.ClampToContent();
        if (controller.Scroll == before)
        {
            return;
        }

        panel.Offset = controller.Scroll;
        ui.Arrange();
    }

    // called on disconnect
    public void Reset()
    {
        bool wasOpen = controller.IsOpen;
        controller.SetOpen(false);
        controller.Reset();
        data.Active = false;
        host.IsVisible = false;
        if (wasOpen)
        {
            // release the keyboard explicitly, a stale owner would block opening the chat
            context.UIInput.SetFocus(UIInputScope.Neutral);
        }

        built = null;
    }

    // the reaction runs before Update so opening the list still happens in one place: it needs the
    // scene check that only Update can do.
    private void RecordToggleRequest()
    {
        if (MiaoNetModule.Settings.PlayerListButtonMode != ButtonMode.Press)
        {
            return;
        }

        toggleRequested = true;
    }

    private void RebuildIfNeeded(float scale, bool liveMode)
    {
        // live mode masks map and room names, so it changes the model too
        var current = new BuiltContent(data.UIVersion, scale, liveMode);
        if (built == current)
        {
            return;
        }

        built = current;
        panel.Rebuild(
            data.BuildUIChannels(),
            data.UIIcons,
            scale,
            MiaoNetFont.ENZhsLineHeight * scale);
    }
}
