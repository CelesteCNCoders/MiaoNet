using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// maps the integer ui scale setting to the real scale factor applied to chat and player
// list geometry. exponential ramp from MinScale to MaxScale over settings 1..20, so each step
// is a constant ratio instead of a constant increment.
public static class UiScale
{
    public const int MinSetting = 1;

    public const int MaxSetting = 20;

    public const float MinScale = 0.25f;

    public const float MaxScale = 0.8f;

    public static float FromSetting(int setting)
    {
        float t = Math.Clamp(
            (setting - MinSetting) / (float)(MaxSetting - MinSetting),
            0f,
            1f);

        return MinScale * (float)Math.Pow(MaxScale / MinScale, t);
    }
}
