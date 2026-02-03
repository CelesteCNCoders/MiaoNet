namespace Celeste.Mod.ChatInputBox;

public interface ITextRenderer
{
    public float LineHeight { get; }

    public bool CanRender(int character);

    public Vector2 Measure(string text);

    public void Draw(string text, Vector2 position, Vector2 justify, Color color);

    public void DrawOutline(string text, Vector2 position, Vector2 justify, Color color);
}