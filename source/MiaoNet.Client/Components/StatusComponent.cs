using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Status;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet;

// The cog at bottom left. This is treated specially because
// it should be updated even without state and connection
public sealed class StatusComponent : MiaoNetComponent
{
    private const float Acceleration = 24f;
    private const float MaxSpinSpeed = 8f;
    private const float Duration = 6f;
    private const float FadeDuration = 1f / 12f;

    private readonly UIRoot ui = new();
    private readonly MiaoNetUICanvas canvas = new();

    private StatusCogwheelNode? cog;
    private TextNode? message;
    private StatusPanelNode? panel;

    private bool spinning;
    private float spinSpeed;
    private float timer;
    private float ease;
    private string? statusMessage;
    private float rotation;

    public StatusComponent(MiaoNetContext context)
        : base(context)
    {
    }

    public void ShowStatusMessage(string message, bool spin = false)
    {
        spinning = spin;
        timer = Duration;
        statusMessage = message;
    }

    public override void Update()
    {
        if (statusMessage is null)
            return;

        if (timer > 0f && ease < 1f)
        {
            ease += 1f / FadeDuration * Engine.RawDeltaTime;
            if (ease > 1f)
                ease = 1f;
        }

        if (timer > 0f && !spinning)
        {
            timer -= Engine.RawDeltaTime;
            if (timer <= 0f)
                timer = 0f;
        }

        if (timer == 0f)
        {
            if (ease > 0f)
            {
                ease -= 1f / FadeDuration * Engine.RawDeltaTime;
                if (ease < 0f)
                    ease = 0f;
            }
            else
            {
                statusMessage = null;
                timer = 0f;
                rotation = 0f;
            }
        }

        if (spinning)
            spinSpeed = Calc.Approach(spinSpeed, MaxSpinSpeed, Acceleration * Engine.RawDeltaTime);
        else
            spinSpeed = Calc.Approach(spinSpeed, 0, Acceleration * 1.5f * Engine.RawDeltaTime);

        rotation += spinSpeed * Engine.RawDeltaTime;
        rotation = Calc.WrapAngle(rotation);
    }

    // drawn through the retained UI, but from here instead of renderableComponents: the status
    // has to show while connecting and after a disconnect, exactly when the components list is
    // not rendered. the call site stays unconditional: this component must render regardless of
    // connection state.
    public override void Render()
    {
        if (statusMessage is null)
            return;

        if (timer > 0f || ease > 0f)
        {
            EnsureNodes();

            UIColor color = UIColor.White * ease;
            cog!.Rotation = rotation;
            cog.Tint = color.ToXna();
            message!.Text = statusMessage;
            message.TextStyle = message.TextStyle with { Color = color };

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

    // defer the texture lookup to first use so this can be built before the game content is
    // ready.
    private void EnsureNodes()
    {
        if (panel is not null)
        {
            return;
        }

        cog = new StatusCogwheelNode(GFX.Gui["reloader/cogwheel"]);
        message = new TextNode
        {
            Style = new UIStyle { TextRenderer = MiaoNetTextRenderer.Instance },
            TextStyle = new TextStyle
            {
                Scale = 1f,
                LineHeight = MiaoNetFont.ENZhsLineHeight,
                HorizontalAnchor = HorizontalAnchor.Left,
                VerticalAnchor = VerticalAnchor.Bottom,
                Decorations = TextDecoration.Outline,
            },
        };

        panel = new StatusPanelNode(cog, message);
        ui.SetRoot(panel);
    }
}
