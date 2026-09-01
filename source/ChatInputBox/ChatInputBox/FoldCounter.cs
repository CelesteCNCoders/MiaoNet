namespace Celeste.Mod.ChatInputBox;

// Animation math of the repeat counter shown on folded messages.
// Free of game types so it can be unit tested; callers turn RgbColor into their own color type.
public static class FoldCounter
{
    public const float PopDuration = 0.5f;
    public const float MaxScale = 2.2f;
    public const float MaxShakeAmplitude = 6f;

    // seconds of one full hue cycle once the count passes MaxRedCount
    public const float RainbowCyclePeriod = 3f;

    // the count where the color reaches full red, beyond it the color turns rainbow
    public const int MaxRedCount = 20;

    public static float GetScale(int count)
        => count <= 1 ? 1f : Math.Min(1f + 0.12f * (count - 1), MaxScale);

    // popProgress is elapsed/duration in [0, 1], expected to be eased by the caller
    public static float GetPopScale(float popProgress)
        => popProgress >= 1f ? 0f : 0.4f * (1f - popProgress);

    public static float GetShakeAmplitude(int count)
        => count < 3 ? 0f : Math.Min(0.5f * (count - 2), MaxShakeAmplitude);

    public static RgbColor GetColor(int count, float animClock)
    {
        if (count <= MaxRedCount)
        {
            float redness = Math.Clamp((count - 2) / (float)(MaxRedCount - 2), 0f, 1f);
            return new RgbColor(1f, 1f - redness, 1f - redness);
        }

        float hue = animClock / RainbowCyclePeriod % 1f;
        if (hue < 0f)
            hue += 1f;
        return HsvToRgb(hue, 1f, 1f);
    }

    // hue in [0, 1] wraps around, saturation and value in [0, 1]
    public static RgbColor HsvToRgb(float hue, float saturation, float value)
    {
        hue -= MathF.Floor(hue);
        float chroma = value * saturation;
        float second = chroma * (1f - MathF.Abs(hue * 6f % 2f - 1f));
        float min = value - chroma;

        return MathF.Floor(hue * 6f) switch
        {
            0 => new RgbColor(chroma + min, second + min, min),
            1 => new RgbColor(second + min, chroma + min, min),
            2 => new RgbColor(min, chroma + min, second + min),
            3 => new RgbColor(min, second + min, chroma + min),
            4 => new RgbColor(second + min, min, chroma + min),
            _ => new RgbColor(chroma + min, min, second + min),
        };
    }
}

public readonly record struct RgbColor(float R, float G, float B);
