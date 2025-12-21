using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.MiaoNet;

public sealed class PauseUpdatedBurst : DisplacementRenderer.Burst
{
    public PauseUpdatedBurst(MTexture texture, Vector2 position, Vector2 origin, float duration)
        : base(texture, position, origin, duration)
    {
    }

    public static PauseUpdatedBurst AddBurstTo(
        DisplacementRenderer renderer,
        Vector2 position, float duration,
        float radiusFrom, float radiusTo,
        float alpha = 1f,
        Ease.Easer? alphaEaser = null
    )
    {
        MTexture mTexture = GFX.Game["util/displacementcircle"];
        PauseUpdatedBurst burst = new PauseUpdatedBurst(mTexture, position, mTexture.Center, duration);
        burst.ScaleFrom = radiusFrom / (float)(mTexture.Width / 2);
        burst.ScaleTo = radiusTo / (float)(mTexture.Width / 2);
        burst.AlphaFrom = alpha;
        burst.AlphaTo = 0f;
        burst.AlphaEaser = alphaEaser;
        renderer.Add(burst);
        return burst;
    }

    public static void Update(DisplacementRenderer renderer)
    {
        for (int num = renderer.points.Count - 1; num >= 0; num--)
        {
            var burst = renderer.points[num];
            if (burst is not PauseUpdatedBurst)
                continue;
            if (burst.Percent >= 1f)
            {
                renderer.points.RemoveAt(num);
            }
            else
            {
                burst.Percent += Engine.RawDeltaTime / burst.Duration;
            }
        }
    }
}
