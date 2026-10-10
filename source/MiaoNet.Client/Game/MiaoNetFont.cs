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

    public static Vector2 MeasureLiteral(string text)
    {
        (_, List<float> lineWidths) = LayoutText(text, 0, text.Length);

        float width = 0f;
        foreach (float lineWidth in lineWidths)
            width = Math.Max(width, lineWidth);

        return new Vector2(width, lineWidths.Count * ENZhsFontSize.LineHeight);
    }

    public static void DrawLiteral(string text, Vector2 position, Vector2 justify, Vector2 scale, Color color)
    {
        (List<GlyphLayout> glyphs, List<float> lineWidths) = LayoutText(text, 0, text.Length);
        float height = lineWidths.Count * ENZhsFontSize.LineHeight;

        foreach (var (character, x, lineIndex, _) in glyphs)
        {
            Vector2 pos = position + new Vector2(
                x + character.XOffset - lineWidths[lineIndex] * justify.X,
                lineIndex * ENZhsFontSize.LineHeight + character.YOffset - height * justify.Y
            ) * scale;
            character.Texture.Draw(pos, Vector2.Zero, color, scale);
        }
    }

    public static void DrawLiteral(
        string text, int start, int end,
        IReadOnlyList<ChatInputHighlightSpan> runs, Vector2 position, Vector2 scale
    )
    {
        (List<GlyphLayout> glyphs, _) = LayoutText(text, start, end);
        float lineHeight = ENZhsFontSize.LineHeight;

        int runIndex = 0;
        foreach (var (character, x, lineIndex, index) in glyphs)
        {
            // runs are sorted and cover the text in order, so advance at most once per glyph
            while (runIndex < runs.Count && index >= runs[runIndex].End)
                runIndex++;
            Color color = runIndex < runs.Count && index >= runs[runIndex].Start
                ? runs[runIndex].Color
                : Color.White;

            Vector2 pos = position + new Vector2(x + character.XOffset, lineIndex * lineHeight + character.YOffset) * scale;
            character.Texture.Draw(pos, Vector2.Zero, color, scale);
        }
    }

    private readonly record struct GlyphLayout(PixelFontCharacter Character, float X, int LineIndex, int Index);

    // Lays out a raw string like PixelFontSize: glyph lookup, kerning and line breaks.
    // Unlike the engine, it does not expand ":emoji:" via Emoji.Apply,
    // so callers get the literal text; the literal APIs stay self-consistent.
    // Keep this in sync if the engine's layout changes.
    private static (List<GlyphLayout> Glyphs, List<float> LineWidths) LayoutText(
        string text, int start, int end
    )
    {
        PixelFontSize font = ENZhsFontSize;
        List<GlyphLayout> glyphs = [];
        List<float> lineWidths = [0f];
        float x = 0f;

        for (int i = start; i < end;)
        {
            int codePoint = ReadCodePoint(text, i, out int length);

            // Only LF breaks a line, matching PixelFontSize.
            if (codePoint == '\n')
            {
                x = 0f;
                lineWidths.Add(0f);
                i += length;
                continue;
            }

            if (font.Characters.TryGetValue(codePoint, out var character))
            {
                int nextCodePoint = PeekCodePoint(text, i + length);
                int kerning = nextCodePoint != -1 && character.Kerning.TryGetValue(nextCodePoint, out int value)
                    ? value
                    : 0;
                glyphs.Add(new GlyphLayout(character, x, lineWidths.Count - 1, i));
                x += character.XAdvance + kerning;
                lineWidths[^1] = Math.Max(lineWidths[^1], x);
            }

            i += length;
        }

        return (glyphs, lineWidths);
    }

    private static int ReadCodePoint(string text, int i, out int length)
    {
        char c = text[i];
        if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
        {
            length = 2;
            return char.ConvertToUtf32(c, text[i + 1]);
        }
        length = 1;
        return c;
    }

    private static int PeekCodePoint(string text, int i)
    {
        if (i >= text.Length)
            return -1;

        char c = text[i];
        if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            return char.ConvertToUtf32(c, text[i + 1]);
        return c;
    }

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
