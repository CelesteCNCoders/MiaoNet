using System.Diagnostics;
using System.Runtime.CompilerServices;
using Celeste.Mod.ChatInputBox;

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
        if (Dialog.Languages is { Count: 0 })
            throw new InvalidOperationException();

        // we also prevent the game from unloading schinese font textures
        // see MiaoNetModule.LanguageSelectUI_SetNextLanguage
        Language langEN = Dialog.Languages["english"];
        Language langZhs = Dialog.Languages["schinese"];
        Fonts.Load(langZhs.FontFace); // schinese is not always loaded
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
        float alpha = (color.A / 255f);
        alpha = MathF.Pow(alpha, 3f);
        ENZhsFont.DrawOutline(ENZhsBaseSize, text, position, justify, scale, color, stroke, strokeColor * alpha);
    }

    [MethodImpl(MioAI)]
    public static void DrawOutline(
        string text, Vector2 position,
        Vector2 justify, Vector2 scale,
        Color color
    )
    {
        float alpha = (color.A / 255f);
        alpha = MathF.Pow(alpha, 3f);
        ENZhsFont.DrawOutline(
            ENZhsBaseSize, text, position,
            justify, scale, color,
            2f, Color.Black * alpha
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

    [MethodImpl(MioAI)]
    public static bool CanRender(int character)
        => ENZhsFontSize.Characters.ContainsKey(character);

    public static void Draw(ChatText text, Vector2 position, float yJustify, Vector2 scale, float alpha)
    {
        float curX = position.X;
        float curY = position.Y;


        foreach (var seg in text.Segments)
        {
            Vector2 size = Measure(seg.Text);
            size *= scale;

            if (!seg.Style.HasFlag(ChatTextStyle.Outline))
            {
                Draw(
                    seg.Text,
                    new Vector2(curX, curY),
                    new Vector2(0f, yJustify),
                    scale,
                    seg.Color * alpha
                );
            }
            else
            {
                DrawOutline(
                    seg.Text,
                    new Vector2(curX, curY),
                    new Vector2(0f, yJustify),
                    scale,
                    seg.Color * alpha
                );
            }

            if (seg.Style.HasFlag(ChatTextStyle.Underscore))
            {
                float lineHeight = ENZhsLineHeight * scale.Y;
                float thinkness = Math.Max(2f, scale.Y * 4f * lineHeight / 96f);

                float yOffset = size.Y * (1f - yJustify);
                Monocle.Draw.Line(
                    new Vector2(curX, curY + yOffset),
                    new Vector2(curX + size.X, curY + yOffset),
                    seg.Color * alpha,
                    thinkness
                );
            }

            if (seg.Style.HasFlag(ChatTextStyle.Strikethrough))
            {
                float lineHeight = ENZhsLineHeight * scale.Y;
                float thinkness = Math.Max(2f, scale.Y * 4f * lineHeight / 96f);

                float yOffset = size.Y * (1f - yJustify) - size.Y / 2f;
                Monocle.Draw.Line(
                    new Vector2(curX, curY + yOffset),
                    new Vector2(curX + size.X, curY + yOffset),
                    seg.Color * alpha,
                    thinkness
                );
            }

            curX += size.X;
        }
    }

    public static float Measure(ChatText text)
    {
        float width = 0f;
        foreach (var seg in text.Segments)
            width += Measure(seg.Text).X;
        return width;
    }
}