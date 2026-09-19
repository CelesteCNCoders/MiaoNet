namespace Celeste.Mod.MiaoNet.UI.Chat;

// layout constants of the chat UI.
public static class ChatLayout
{
    // outer margin around the chat.
    public const float Margin = 16f;

    // padding inside the list / input box.
    public const float Padding = 8f;

    // horizontal padding around a message row.
    public const float MessagePaddingX = 8f;

    // width of "00:00:00" in units of the line height.
    public const float TimeTextWidthRatio = 3.5625f;

    // padding inside the timestamp cell.
    public const float TimeTextPaddingX = 2f;

    // gap before the X<n> counter, scaled by the UI scale.
    public const float CounterGap = 4f;

    // seconds a message takes to fade away.
    public const float DisappearDuration = 0.25f;

    // keyboard scroll speed, pixels per second.
    public const float KeyboardScrollSpeed = 1024f;

    // gap between two tabs.
    public const float TabGap = 2f;

    // monocle's Ease.ElasticOut, inlined so the UI layer doesn't pull in game types.
    public static float ElasticOut(float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return (33f * t3 * t2) + (-106f * t2 * t2) + (126f * t3) + (-67f * t2) + (15f * t);
    }
}
