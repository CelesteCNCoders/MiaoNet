namespace Celeste.Mod.MiaoNet;

public static class MiaoNetFont
{
    public static Language ENLanguage => Dialog.Languages["english"];

    public static Language ZhsLanguage => Dialog.Languages["schinese"];

    public static PixelFont Font => Fonts.Get(ENLanguage.FontFace);

    public static PixelFont ZhsFont => Fonts.Get(ZhsLanguage.FontFace);

    public static float BaseSize => ENLanguage.FontFaceSize;

    public static PixelFontSize FontSize => Font.Get(BaseSize);

    public static int LineHeight => ENLanguage.FontSize.LineHeight;

    public static void DrawGhostName(string name, Vector2 position, Color color)
        => Font.DrawOutline(
            BaseSize,
            name,
            position,
            new(0.5f, 1.0f), new(0.5f, 0.5f),
            color, 2f,
            Color.Black with { A = color.A }
        );

    public static void DrawPlayerListEntry(string text, Vector2 position, Color color, float scale)
        => Font.Draw(
            BaseSize,
            text,
            position,
            new(0f, 0f), new(scale, scale),
            color
        );

    public static void DrawGhostEmoteText(string text, Vector2 position, Color color, float scale)
        => ZhsFont.DrawOutline(
            BaseSize,
            text,
            position,
            new(0.5f, 1f), new(scale, scale),
            color, 2f,
            Color.Black with { A = color.A }
        );

    public static Vector2 MeasurePlayerListEntry(string text)
        => FontSize.Measure(text);
}