using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.MiaoNet;

// The cog at bottom left. This is treated specially because
// it should be updated even without state and connection
public sealed class StatusComponent : MiaoNetComponent
{
    private const float Accelerate = 24f;
    private const float MaxSpinSpeed = 8f;

    private bool spinning;
    private float spinSpeed;
    private float statusMessageTimer;
    private string? statusMessage;
    private float rotation;

    public StatusComponent(MiaoNetContext context)
        : base(context)
    {
    }

    public void ShowStatusMessage(string message, bool spin = false)
    {
        spinning = spin;
        statusMessageTimer = 6f;
        statusMessage = message;
    }

    public override void Update()
    {
        if (statusMessageTimer > 0f && !spinning)
        {
            statusMessageTimer -= Engine.RawDeltaTime;
            if (statusMessageTimer <= 0f)
            {
                statusMessage = null;
                rotation = 0f;
            }
        }

        if (spinning)
            spinSpeed = Calc.Approach(spinSpeed, MaxSpinSpeed, Accelerate * Engine.RawDeltaTime);
        else
            spinSpeed = Calc.Approach(spinSpeed, 0, Accelerate * 1.5f * Engine.RawDeltaTime);

        rotation += spinSpeed * Engine.RawDeltaTime;
        rotation = Calc.WrapAngle(rotation);
    }

    public override void Render()
    {
        if (statusMessageTimer > 0f)
        {
            var tex = GFX.Gui["reloader/cogwheel"];
            Vector2 pos = new Vector2(64f, Engine.Height - 64f);
            const float Scale = 1f / 3.5f;
            tex.DrawOutlineCentered(pos + new Vector2(tex.Width, -tex.Height) / 2f * Scale, Color.White, Scale, rotation);
            pos.X += tex.Width * Scale + 32f;
            MiaoNetFont.DrawOutline(statusMessage!, pos, Vector2.UnitY, Vector2.One, Color.White);
        }
    }
}
