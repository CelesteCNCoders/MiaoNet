using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

public static class MiaoNetFont
{
    private const MethodImplOptions MioAI = MethodImplOptions.AggressiveInlining;

    // use en font first, if not found, then fallback to zhs font
    // will everest support pixel font fallbacking?
    public static PixelFont ENZhsFont { get; }

    public static float ENZhsBaseSize { get; }

    public static PixelFontSize ENZhsFontSize => ENZhsFont.Get(ENZhsBaseSize);

    public static int ENZhsLineHeight => ENZhsFontSize.LineHeight;

    static MiaoNetFont()
    {
        // don't trigger cctor call too early...
        if (Dialog.Languages is not { Count: not 0 })
            throw new InvalidOperationException();

        Language langEN = Dialog.Languages["english"];
        Language langZhs = Dialog.Languages["schinese"];
        ENZhsBaseSize = langEN.FontFaceSize;
        PixelFont font = SimpleMergeFont(langEN.Font, langZhs.Font);
        ENZhsFont = font;
    }

    private static PixelFont SimpleMergeFont(PixelFont first, PixelFont second)
    {
        PixelFont font = new("MiaoNetFont");
        font.managedTextures = first.managedTextures.Union(second.managedTextures).ToList();
        foreach (var size in second.Sizes)
        {
            PixelFontSize sizeClone = new()
            {
                LineHeight = size.LineHeight,
                Outline = size.Outline,
                Size = size.Size
            };
            sizeClone.Characters = new(size.Characters);
            font.Sizes.Add(sizeClone);
        }
        foreach (var size in first.Sizes)
        {
            var pixelFontSize = font.Sizes.FirstOrDefault(s => s.Size == size.Size);
            if (pixelFontSize is null)
                continue;
            foreach (var pair in size.Characters)
                pixelFontSize.Characters[pair.Key] = pair.Value;
        }
        return font;
    }

    // we just want to make these methods like macros instead of methods
    // so mark them with AggressiveInlining

    [MethodImpl(MioAI)]
    public static void Draw(string text, Vector2 position, Vector2 justify, Vector2 scale, Color color)
        => ENZhsFont.Draw(ENZhsBaseSize, text, position, justify, scale, color);

    [MethodImpl(MioAI)]
    public static void DrawOutline(
        string text, Vector2 position,
        Vector2 justify, Vector2 scale,
        Color color,
        float stroke, Color strokeColor
    )
    {
        ENZhsFont.DrawOutline(ENZhsBaseSize, text, position, justify, scale, color, stroke, strokeColor);
    }

    [MethodImpl(MioAI)]
    public static void DrawOutline(
        string text, Vector2 position,
        Vector2 justify, Vector2 scale,
        Color color
    )
    {
        ENZhsFont.DrawOutline(
            ENZhsBaseSize, text, position,
            justify, scale, color,
            2f, Color.Black with { A = color.A }
        );
    }

    [MethodImpl(MioAI)]
    public static void DrawOutline(string text, Vector2 position, Color color)
        => DrawOutline(text, position, Vector2.Zero, Vector2.One, color);

    [MethodImpl(MioAI)]
    public static void DrawOutlineBottomCentered(string text, Vector2 position, Vector2 scale, Color color)
        => DrawOutline(text, position, new Vector2(0.5f, 1.0f), scale, color);

    [MethodImpl(MioAI)]
    public static Vector2 Measure(string text)
        => ENZhsFontSize.Measure(text);

    public static bool CanRender(int character)
        => ENZhsFontSize.Characters.ContainsKey(character);
}