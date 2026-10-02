using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.PlayerList;

// widths derived from the player list contents: the widest row sets a common panel width so every
// channel lines up, and the widest ping sets a fixed right-hand column.
//
// it also carries the measuring context (renderer, scale, line height) that every row and channel
// needs, so those don't have to be threaded through each node constructor.
public sealed record PlayerListMetrics(
    ITextRenderer Renderer,
    float Scale,
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

    // the text style every piece of player list text starts from; callers add colour and anchors
    public TextStyle TextStyle => new() { Scale = Scale, LineHeight = LineHeight };

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

        TextStyle probe = new() { Scale = scale, LineHeight = lineHeight };
        float spaceWidth = renderer.Measure(" ", probe).Width;
        float colonWidth = renderer.Measure(":", probe).Width;

        // the widths aren't known yet; the row measuring below only needs the context
        var metrics = new PlayerListMetrics(renderer, scale, 0f, 0f, 0f, spaceWidth, colonWidth, lineHeight);

        float maxPingWidth = 0f;
        float maxLineWidth = 0f;

        foreach (PlayerListChannel channel in channels)
        {
            maxLineWidth = MathF.Max(maxLineWidth, renderer.Measure(channel.Header, metrics.TextStyle).Width);

            foreach (PlayerRow row in channel.Rows)
            {
                maxLineWidth = MathF.Max(maxLineWidth, metrics.RowContentWidth(row, icons));

                if (row.PingText is { Length: > 0 } ping)
                {
                    maxPingWidth = MathF.Max(
                        maxPingWidth,
                        renderer.Measure(ping, metrics.TextStyle).Width + spaceWidth);
                }
            }
        }

        float rowWidth = maxLineWidth + maxPingWidth + (2f * PlayerListLayout.RowPaddingX);
        return metrics with
        {
            RowWidth = rowWidth,
            PanelWidth = rowWidth + (2f * PlayerListLayout.ChannelPaddingX),
            PingColumnWidth = maxPingWidth,
        };
    }

    // left-to-right icon order. the sequence is part of the look, so don't reorder it casually --
    // and it has to be the same list the rows build from, or the measured width won't match.
    public static readonly PlayerStatus[] StatusOrder =
    [
        PlayerStatus.Paused,
        PlayerStatus.Interactions,
        PlayerStatus.LiveMode,
        PlayerStatus.TakingGolden,
        PlayerStatus.GroupPhotoMode,
        PlayerStatus.Watching,
    ];

    // icons are scaled so their height matches the line height, so work the width out from that
    private float IconWidth(IUITexture texture)
        => texture.Height <= 0f ? 0f : LineHeight / texture.Height * texture.Width;

    private float RowContentWidth(PlayerRow row, PlayerListIcons icons)
    {
        float width = Renderer.Measure(row.DisplayName, TextStyle).Width + PlayerListLayout.MiddlePadding;

        foreach (PlayerStatus status in StatusOrder)
        {
            if (!row.Status.HasFlag(status) || icons.For(status) is not { } texture)
            {
                continue;
            }

            width += IconWidth(texture);
            if (status == PlayerStatus.Paused)
            {
                width += 2f * PlayerListLayout.PausedGap;
            }
        }

        if (!row.HasLocation)
        {
            return width;
        }

        width += ColonWidth;

        if (row.UsesDebugRoomIcon && icons.DebugMap is { } debugMap)
        {
            width += IconWidth(debugMap);
        }
        else
        {
            width += Renderer.Measure(row.RoomText ?? string.Empty, TextStyle).Width;
        }

        if (row.AreaIcon is { } areaIcon)
        {
            width += SpaceWidth + IconWidth(areaIcon);
        }

        width += SpaceWidth;
        width += Renderer.Measure(row.MapName ?? string.Empty, TextStyle).Width;

        if (row.AreaModeText is { Length: > 0 } areaMode)
        {
            width += SpaceWidth + Renderer.Measure(areaMode, TextStyle).Width;
        }

        return width;
    }
}
