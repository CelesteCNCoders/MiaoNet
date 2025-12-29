namespace Celeste.Mod.MiaoNet;

public static class ScreenClamper
{
    public static Vector2 ClampIntoScreen(
        Vector2 position,
        Vector2 size,
        Vector2 justify,
        float margin
    )
    {
        Vector2 topLeft = new Vector2(margin + size.X * justify.X, margin + size.Y * justify.Y);
        Vector2 bottomRight = new Vector2(
            Engine.Width - margin - size.X * (1f - justify.X),
            Engine.Height - margin - size.Y * (1f - justify.Y)
        );

        return Vector2.Clamp(position, topLeft, bottomRight);
    }
}
