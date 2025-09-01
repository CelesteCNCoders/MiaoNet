namespace Celeste.Mod.MiaoNet;

public static class MiaoNetFont
{
    public static PixelFont Font => Fonts.Get(Dialog.Languages["english"].FontFace);

    public static float BaseSize => Dialog.Languages["english"].FontFaceSize;

    public static void DrawGhostName(string name, Vector2 position, Color color)
        => Font.DrawOutline(BaseSize, name, position, new(0.5f, 1.0f), new(0.5f, 0.5f), color, 2f, Color.Black with { A = color.A });
}