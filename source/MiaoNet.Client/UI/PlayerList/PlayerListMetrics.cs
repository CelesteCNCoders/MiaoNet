using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.PlayerList;

// widths derived from the player list contents: the widest row sets a common panel width so
// every channel lines up, and the widest ping sets a fixed right-hand column.
// pure computation over the XNA-free view model and an ITextRenderer, so it can be unit tested
// with a fake renderer.
public sealed record PlayerListMetrics(
    float RowWidth,
    float PanelWidth,
    float PingColumnWidth,
    float SpaceWidth,
    float ColonWidth,
    float LineHeight)
{
    // line height plus the row padding on both sides
    public float RowHeight => LineHeight + (2f * PlayerListLayout.RowPaddingY);

    public float HeaderHeight => LineHeight;

    public TextStyle TextStyle(float scale)
        => new() { Scale = scale, LineHeight = LineHeight };

    public static PlayerListMetrics Compute(
        IReadOnlyList<PlayerListChannel> channels,
        ITextRenderer renderer,
        float scale,
        float lineHeight,
        PlayerListIcons icons)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(icons);

        TextStyle style = new() { Scale = scale, LineHeight = lineHeight };
        float spaceWidth = renderer.Measure(" ", style).Width;
        float colonWidth = renderer.Measure(":", style).Width;

        float maxPingWidth = 0f;
        float maxLineWidth = 0f;

        foreach (PlayerListChannel channel in channels)
        {
            maxLineWidth = MathF.Max(maxLineWidth, renderer.Measure(channel.Header, style).Width);

            foreach (PlayerRow row in channel.Rows)
            {
                maxLineWidth = MathF.Max(
                    maxLineWidth,
                    MeasureRowContentWidth(row, renderer, style, lineHeight, icons, spaceWidth, colonWidth));

                if (row.PingText is { Length: > 0 } ping)
                {
                    maxPingWidth = MathF.Max(
                        maxPingWidth,
                        renderer.Measure(ping, style).Width + spaceWidth);
                }
            }
        }

        // panel width: widest line plus the row padding on both sides
        float rowWidth = maxLineWidth + maxPingWidth + (2f * PlayerListLayout.RowPaddingX);
        float panelWidth = rowWidth + (2f * PlayerListLayout.ChannelPaddingX);
        return new PlayerListMetrics(rowWidth, panelWidth, maxPingWidth, spaceWidth, colonWidth, lineHeight);
    }

    // icons are scaled so their height matches the line height, so work the width out from that
    public static float IconWidth(IUITexture texture, float lineHeight)
        => texture.Height <= 0f ? 0f : lineHeight / texture.Height * texture.Width;

    private static float MeasureRowContentWidth(
        PlayerRow row,
        ITextRenderer renderer,
        TextStyle style,
        float lineHeight,
        PlayerListIcons icons,
        float spaceWidth,
        float colonWidth)
    {
        float width = renderer.Measure(row.DisplayName, style).Width + PlayerListLayout.MiddlePadding;

        foreach (PlayerStatus status in StatusOrder)
        {
            if (!row.Status.HasFlag(status))
            {
                continue;
            }

            IUITexture? texture = icons.For(status);
            if (texture is null)
            {
                continue;
            }

            width += IconWidth(texture, lineHeight);
            if (status == PlayerStatus.Paused)
            {
                width += 2f * PlayerListLayout.PausedGap;
            }
        }

        if (!row.HasLocation)
        {
            return width;
        }

        width += colonWidth;

        if (row.UsesDebugRoomIcon && icons.DebugMap is { } debugMap)
        {
            width += IconWidth(debugMap, lineHeight);
        }
        else
        {
            width += renderer.Measure(row.RoomText ?? string.Empty, style).Width;
        }

        if (row.AreaIcon is { } areaIcon)
        {
            width += spaceWidth + IconWidth(areaIcon, lineHeight);
        }

        width += spaceWidth;
        width += renderer.Measure(row.MapName ?? string.Empty, style).Width;

        if (row.AreaModeText is { Length: > 0 } areaMode)
        {
            width += spaceWidth + renderer.Measure(areaMode, style).Width;
        }

        return width;
    }

    // left-to-right icon order. the sequence is part of the look, so
    // don't reorder it casually
    public static readonly PlayerStatus[] StatusOrder =
    [
        PlayerStatus.Paused,
        PlayerStatus.Interactions,
        PlayerStatus.LiveMode,
        PlayerStatus.TakingGolden,
        PlayerStatus.GroupPhotoMode,
        PlayerStatus.Watching,
    ];
}
