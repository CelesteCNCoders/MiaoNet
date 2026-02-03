using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Celeste.Mod.ChatInputBox;

[DebuggerDisplay("Count = {Segments.Count}")]
public sealed class ChatText
{
    public static readonly ImmutableArray<Color> CommonColors;

    public ImmutableArray<ChatTextSegment> Segments { get; }

    static ChatText()
    {
        CommonColors = [
            new Color(0x00, 0x00, 0x00, 0xFF), // 0 black
            new Color(0x00, 0x00, 0xAA, 0xFF), // 1 dark blue
            new Color(0x00, 0xAA, 0x00, 0xFF), // 2 dark green
            new Color(0x00, 0xAA, 0xAA, 0xFF), // 3 dark aqua
            new Color(0xAA, 0x00, 0x00, 0xFF), // 4 dark red
            new Color(0xAA, 0x00, 0xAA, 0xFF), // 5 dark purple
            new Color(0xFF, 0xAA, 0x00, 0xFF), // 6 gold
            new Color(0xAA, 0xAA, 0xAA, 0xFF), // 7 gray
            new Color(0x55, 0x55, 0x55, 0xFF), // 8 dark gray
            new Color(0x55, 0x55, 0xFF, 0xFF), // 9 blue
            new Color(0x55, 0xFF, 0x55, 0xFF), // a green
            new Color(0x55, 0xFF, 0xFF, 0xFF), // b aqua
            new Color(0xFF, 0x55, 0x55, 0xFF), // c red
            new Color(0xFF, 0x55, 0xFF, 0xFF), // d light purple
            new Color(0xFF, 0xFF, 0x55, 0xFF), // e yellow
            new Color(0xFF, 0xFF, 0xFF, 0xFF), // f white
        ];
    }

    public ChatText(ImmutableArray<ChatTextSegment> segments)
        => Segments = segments;

    public static ImmutableArray<ChatTextSegment> Parse(ReadOnlySpan<char> input, Color defaultColor)
    {
        #region don't try studying something from these ai generated code
        if (input.IsEmpty)
            return ImmutableArray<ChatTextSegment>.Empty;

        var segmentsBuilder = ImmutableArray.CreateBuilder<ChatTextSegment>();

        var currentColor = defaultColor;
        var currentStyle = ChatTextStyle.None;

        var sb = new StringBuilder(input.Length);

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '\\' && i + 1 < input.Length)
            {
                char nextChar = input[i + 1];
                bool stateChanged = false;

                switch (nextChar)
                {
                case '\\':
                    sb.Append('\\');
                    i++;
                    continue;

                case 'r':
                    FlushSegment(segmentsBuilder, sb, currentStyle, currentColor);
                    currentColor = defaultColor;
                    currentStyle = ChatTextStyle.None;
                    stateChanged = true;
                    break;
                case 'u':
                    FlushSegment(segmentsBuilder, sb, currentStyle, currentColor);
                    currentStyle ^= ChatTextStyle.Underscore;
                    stateChanged = true;
                    break;
                case 's':
                    FlushSegment(segmentsBuilder, sb, currentStyle, currentColor);
                    currentStyle ^= ChatTextStyle.Strikethrough;
                    stateChanged = true;
                    break;
                case 'o':
                    FlushSegment(segmentsBuilder, sb, currentStyle, currentColor);
                    currentStyle ^= ChatTextStyle.Outline;
                    stateChanged = true;
                    break;

                case '#':
                    if (i + 7 < input.Length && TryParseHexColor(input.Slice(i + 2, 6), out Color customColor))
                    {
                        FlushSegment(segmentsBuilder, sb, currentStyle, currentColor);
                        currentColor = customColor;
                        i += 7;
                        continue;
                    }
                    else
                    {
                        sb.Append('\\');
                    }
                    break;

                default:
                    int colorIndex = GetCommonColorIndex(nextChar);
                    if (colorIndex >= 0)
                    {
                        FlushSegment(segmentsBuilder, sb, currentStyle, currentColor);
                        if (colorIndex < CommonColors.Length)
                        {
                            currentColor = CommonColors[colorIndex];
                        }
                        stateChanged = true;
                    }
                    else
                    {
                        sb.Append('\\');
                    }
                    break;
                }

                if (stateChanged)
                {
                    i++;
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        FlushSegment(segmentsBuilder, sb, currentStyle, currentColor);

        return segmentsBuilder.DrainToImmutable();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void FlushSegment(
            ImmutableArray<ChatTextSegment>.Builder builder,
            StringBuilder sb,
            ChatTextStyle style, Color color
        )
        {
            if (sb.Length > 0)
            {
                builder.Add(new ChatTextSegment(style, color, sb.ToString()));
                sb.Clear();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool TryParseHexColor(ReadOnlySpan<char> hexSpan, out Color color)
        {
            int r1 = GetHexVal(hexSpan[0]);
            int r2 = GetHexVal(hexSpan[1]);
            int g1 = GetHexVal(hexSpan[2]);
            int g2 = GetHexVal(hexSpan[3]);
            int b1 = GetHexVal(hexSpan[4]);
            int b2 = GetHexVal(hexSpan[5]);

            if ((r1 | r2 | g1 | g2 | b1 | b2) == -1)
            {
                color = default;
                return false;
            }

            byte r = (byte)((r1 << 4) | r2);
            byte g = (byte)((g1 << 4) | g2);
            byte b = (byte)((b1 << 4) | b2);

            color = new Color(r, g, b, 255);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int GetCommonColorIndex(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => 10 + (c - 'a'),
            >= 'A' and <= 'F' => 10 + (c - 'A'),
            _ => -1
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int GetHexVal(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'A' and <= 'F' => c - 'A' + 10,
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => -1
        };
        #endregion
    }

    public static ChatText Create(ReadOnlySpan<char> input, Color defaultColor)
        => new(Parse(input, defaultColor));
}