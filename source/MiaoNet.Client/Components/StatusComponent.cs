using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.MiaoNet;

// The cog at bottom left. This is treated specially because
// it should be updated even without state and connection
public sealed class StatusComponent : MiaoNetComponent
{
    private const float Acceleration = 24f;
    private const float MaxSpinSpeed = 8f;
    private const float Duration = 6f;
    private const float FadeDuration = 1f / 12f;

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

    public override void Render()
    {
        if (statusMessage is null)
            return;
        if (timer > 0f || ease > 0f)
        {
            var tex = GFX.Gui["reloader/cogwheel"];
            Vector2 pos = new Vector2(64f, Engine.Height - 64f);
            const float Scale = 1f / 3.5f;
            Color color = Color.White * ease;
            DrawOutlineCentered(tex, pos + new Vector2(tex.Width, -tex.Height) / 2f * Scale, color, Scale, rotation);
            pos.X += tex.Width * Scale + 32f;
            MiaoNetFont.DrawOutline(statusMessage!, pos, Vector2.UnitY, Vector2.One, color);
        }
    }

    private static void DrawOutlineCentered(MTexture texture, Vector2 position, Color color, float scale, float rotation)
    {
        float scaleFix = texture.ScaleFix;
        scale *= scaleFix;
        Rectangle clipRect = texture.ClipRect;
        Vector2 origin = (texture.Center - texture.DrawOffset) / scaleFix;
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                if (i != 0 || j != 0)
                {
                    float alpha = color.A / 255f;
                    Draw.SpriteBatch.Draw(
                        texture.Texture.Texture_Safe,
                        position + new Vector2(i, j),
                        clipRect,
                        Color.Black * MathF.Pow(alpha, 4f), // diff from original DrawOutlineCentered
                        rotation, origin, scale, SpriteEffects.None, 0f
                    );
                }
            }
        }

        Draw.SpriteBatch.Draw(texture.Texture.Texture_Safe, position, clipRect, color, rotation, origin, scale, SpriteEffects.None, 0f);
    }
}
