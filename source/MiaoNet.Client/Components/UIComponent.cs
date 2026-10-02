using Celeste.Mod.MiaoNet.UI.Chat;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Layout;
using Celeste.Mod.MiaoNet.UI.PlayerList;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet;

// the screen-space ui host. owns the retained tree, the canvas and the two panel hosts, and drives
// them in one fixed order per frame.
//
// everything panel-specific lives next to its panel under ui/: ChatPanelHost and
// PlayerListPanelHost read their own settings and input. what's left here is what both panels need
// -- the shared tree, the frame order and the host size.
public sealed class UIComponent : MiaoNetComponent
{
    private readonly UIRoot ui = new();
    private readonly MiaoNetUICanvas canvas = new();
    private readonly MiaoNetTextRenderer textRenderer = MiaoNetTextRenderer.Instance;

    private readonly ChatPanelHost chat;
    private readonly PlayerListPanelHost playerList;
    private readonly StackNode root;

    private float hostWidth = -1f;
    private float hostHeight = -1f;

    public UIComponent(MiaoNetContext context)
        : base(context)
    {
        chat = new ChatPanelHost(context, textRenderer);
        playerList = new PlayerListPanelHost(context, textRenderer);

        // paint order: player list on top of the chat
        root = new StackNode { Alignment = UIAlignment.TopLeft };
        root.Add(chat.Root);
        root.Add(playerList.Root);
        ui.SetRoot(root);
    }

    public override void Update()
    {
        chat.Update();
        playerList.Update();
        EnsureHostSize();
        ui.Layout(hostWidth, hostHeight);

        // after layout: both of these need the rects the layout pass just produced
        playerList.AfterLayout(ui);
        chat.UpdateInputMethodRect();
    }

    public override void Render()
    {
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

    public override void OnDisconnected()
    {
        playerList.Reset();
        chat.Reset();
    }

    private void EnsureHostSize()
    {
        if (hostWidth == Engine.Width && hostHeight == Engine.Height)
        {
            return;
        }

        hostWidth = Engine.Width;
        hostHeight = Engine.Height;
        playerList.SetHostSize(hostWidth, hostHeight);
        root.Style = new UIStyle { Width = hostWidth, Height = hostHeight };
    }
}
