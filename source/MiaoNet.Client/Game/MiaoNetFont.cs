namespace Celeste.Mod.MiaoNet;

public static class MiaoNetFont
{
    public static PixelFont Font => Fonts.Get(Dialog.Languages["english"].FontFace);

    public static float BaseSize => Dialog.Languages["english"].FontFaceSize;

    public static PixelFontSize FontSize => Font.Get(BaseSize);

    public static int LineHeight => Dialog.Languages["english"].FontSize.LineHeight;

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
            Color.Black with { A = color.A }
        );

    public static Vector2 MeasurePlayerListEntry(string text)
        => FontSize.Measure(text);
}