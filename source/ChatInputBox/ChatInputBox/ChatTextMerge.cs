namespace Celeste.Mod.ChatInputBox;

/// <summary>
/// Pure decision/calculation helpers for merging repeated (spam) chat messages
/// into a single line with a "X{n}" repeat counter.
/// Kept free of Monocle/game types so it can be unit tested.
/// </summary>
public static class ChatTextMerge
{
    /// <summary>
    /// Two identical messages arriving within this window (seconds) get merged.
    /// </summary>
    public const float MergeWindowSeconds = 5f;

    /// <summary>
    /// Duration (seconds) of the small pop animation played on the counter
    /// each time the repeat count increments.
    /// </summary>
    public const float MergePopDuration = 0.5f;

    /// <summary>
    /// Maximum scale the repeat counter can grow to.
    /// </summary>
    public const float MaxCounterScale = 2.2f;

    /// <summary>
    /// Maximum per-frame shake offset (in font units, before renderer scale) of the counter.
    /// </summary>
    public const float MaxCounterShakeAmplitude = 6f;

    /// <summary>
    /// Segment-wise equality of two <see cref="ChatText"/>s.
    /// Since sender name/avatar are baked into the segments, equal segments mean
    /// "the same player sent the same text".
    /// </summary>
    public static bool ContentEquals(ChatText a, ChatText b)
    {
        var segmentsA = a.Segments;
        var segmentsB = b.Segments;
        if (segmentsA.Length != segmentsB.Length)
            return false;
        for (int i = 0; i < segmentsA.Length; i++)
        {
            var segA = segmentsA[i];
            var segB = segmentsB[i];
            if (segA.Style != segB.Style
                || segA.Color != segB.Color
                || !string.Equals(segA.Text, segB.Text, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Whether a new message arriving <paramref name="timeSinceLastEvent"/> seconds
    /// after the last arrival/merge of the previous line should be merged into it.
    /// </summary>
    public static bool ShouldMerge(float timeSinceLastEvent)
        => timeSinceLastEvent <= MergeWindowSeconds;

    /// <summary>
    /// Base scale of the "X{n}" counter: grows with n, capped at <see cref="MaxCounterScale"/>.
    /// </summary>
    public static float GetCounterScale(int repeatCount)
        => repeatCount <= 1 ? 1f : Math.Min(1f + 0.12f * (repeatCount - 1), MaxCounterScale);

    /// <summary>
    /// Extra pop scale of the counter, fading out over <see cref="MergePopDuration"/>
    /// after each increment. <paramref name="popProgress"/> is elapsed/duration in [0, 1].
    /// </summary>
    public static float GetCounterPopScale(float popProgress)
        => popProgress >= 1f ? 0f : 0.4f * (1f - popProgress);

    /// <summary>
    /// Per-frame shake amplitude of the counter in font units.
    /// No shake at n &lt;= 2, grows from n &gt;= 3, capped at <see cref="MaxCounterShakeAmplitude"/>.
    /// </summary>
    public static float GetCounterShakeAmplitude(int repeatCount)
        => repeatCount < 3 ? 0f : Math.Min(0.5f * (repeatCount - 2), MaxCounterShakeAmplitude);

    /// <summary>
    /// Lerp factor (white -> red) of the counter color as n grows.
    /// </summary>
    public static float GetCounterColorLerp(int repeatCount)
        => repeatCount <= 1 ? 0f : Math.Min((repeatCount - 1) / 8f, 1f);
}
