using System;
using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Layout;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.PlayerList;

// one player row: zebra stripe, name with status icons, then a right-aligned location group and
// a fixed-width ping column.
// positions come entirely from the flex layout: the name group sits at the start, a spacer
// pushes the location group and ping to the end, and the ping column is a fixed width so pings
// line up across rows. no hand-computed coordinates anywhere.
public sealed class PlayerRowNode : BoxNode
{
    private readonly UIColor stripe;
    private readonly PlayerListIcons icons;
    private IconNode? pausedIcon;

    public PlayerRowNode(PlayerRow row, ITextRenderer renderer, PlayerListMetrics metrics, PlayerListIcons icons, float scale, bool evenRow)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(icons);

        this.icons = icons;
        stripe = evenRow ? MiaoNetUITheme.PlayerList.StripeEven : MiaoNetUITheme.PlayerList.StripeOdd;

        Style = new UIStyle
        {
            Width = metrics.RowWidth,
            Padding = new EdgeInsets(PlayerListLayout.RowPaddingX, PlayerListLayout.RowPaddingY),
            TextRenderer = renderer,
        };

        Child = BuildContent(row, renderer, metrics, scale);
    }

    // apply the floating paused-icon animation
    public void SetPausedIconOffset(float offsetX)
    {
        if (pausedIcon is not null)
        {
            pausedIcon.PaintOffset = new UIOffset(offsetX, 0f);
        }
    }

    protected override void PaintSelf(IUICanvas canvas, float opacity)
    {
        canvas.FillRect(Bounds, stripe * opacity);
        base.PaintSelf(canvas, opacity);
    }

    private FlexNode BuildContent(PlayerRow row, ITextRenderer renderer, PlayerListMetrics metrics, float scale)
    {
        var content = new FlexNode
        {
            Axis = FlexAxis.Horizontal,
            Spacing = 0f,
            CrossAxisAlignment = CrossAxisAlignment.Start,
            Style = new UIStyle { Height = metrics.LineHeight, TextRenderer = renderer },
        };

        content.Add(BuildNameGroup(row, renderer, metrics, scale));
        content.Add(new SpacerNode());
        content.Add(BuildLocationGroup(row, renderer, metrics, scale));
        content.Add(BuildPingColumn(row, renderer, metrics, scale));
        return content;
    }

    private FlexNode BuildNameGroup(PlayerRow row, ITextRenderer renderer, PlayerListMetrics metrics, float scale)
    {
        var group = new FlexNode
        {
            Axis = FlexAxis.Horizontal,
            Spacing = 0f,
            CrossAxisAlignment = CrossAxisAlignment.Start,
            Style = new UIStyle { TextRenderer = renderer },
        };

        group.Add(Text(row.DisplayName, row.NameColor, renderer, metrics, scale));

        foreach (PlayerStatus status in PlayerListMetrics.StatusOrder)
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

            if (status == PlayerStatus.Paused)
            {
                group.Add(Gap(PlayerListLayout.PausedGap));
            }

            var icon = new IconNode { Texture = texture, TargetHeight = metrics.LineHeight, Tint = MiaoNetUITheme.PlayerList.Icon };
            group.Add(icon);

            if (status == PlayerStatus.Paused)
            {
                pausedIcon = icon;
                group.Add(Gap(PlayerListLayout.PausedGap));
            }
        }

        return group;
    }

    private FlexNode BuildLocationGroup(PlayerRow row, ITextRenderer renderer, PlayerListMetrics metrics, float scale)
    {
        var group = new FlexNode
        {
            Axis = FlexAxis.Horizontal,
            Spacing = 0f,
            CrossAxisAlignment = CrossAxisAlignment.Start,
            Style = new UIStyle { TextRenderer = renderer },
        };

        if (!row.HasLocation)
        {
            return group;
        }

        if (row.UsesDebugRoomIcon && icons.DebugMap is { } debugMap)
        {
            group.Add(new IconNode
            {
                Texture = debugMap,
                TargetHeight = metrics.LineHeight,
                Tint = MiaoNetUITheme.PlayerList.Icon,
            });
        }
        else if (row.RoomText is { Length: > 0 } room)
        {
            group.Add(Text(room, MiaoNetUITheme.PlayerList.Room, renderer, metrics, scale));
        }

        group.Add(Text(":", MiaoNetUITheme.PlayerList.Room, renderer, metrics, scale));
        group.Add(Gap(metrics.SpaceWidth));

        if (row.MapName is { Length: > 0 } mapName)
        {
            group.Add(Text(mapName, row.MapNameColor, renderer, metrics, scale));
        }

        if (row.AreaModeText is { Length: > 0 } areaMode)
        {
            group.Add(Gap(metrics.SpaceWidth));
            group.Add(Text(areaMode, row.MapSideColor, renderer, metrics, scale));
        }

        if (row.AreaIcon is { } areaIcon)
        {
            group.Add(Gap(metrics.SpaceWidth));
            group.Add(new IconNode
            {
                Texture = areaIcon,
                TargetHeight = metrics.LineHeight,
                Tint = MiaoNetUITheme.PlayerList.Icon,
            });
        }

        return group;
    }

    private BoxNode BuildPingColumn(PlayerRow row, ITextRenderer renderer, PlayerListMetrics metrics, float scale)
    {
        var column = new BoxNode
        {
            Style = new UIStyle { Width = metrics.PingColumnWidth, TextRenderer = renderer },
        };

        if (row.PingText is { Length: > 0 } ping)
        {
            column.Child = Text(ping, MiaoNetUITheme.PlayerList.Ping, renderer, metrics, scale, HorizontalAnchor.Right);
        }

        return column;
    }

    private static TextNode Text(
        string text,
        UIColor color,
        ITextRenderer renderer,
        PlayerListMetrics metrics,
        float scale,
        HorizontalAnchor anchor = HorizontalAnchor.Left)
        => new()
        {
            Text = text,
            Style = new UIStyle { Foreground = color, TextRenderer = renderer },
            TextStyle = new TextStyle
            {
                Scale = scale,
                LineHeight = metrics.LineHeight,
                Color = color,
                HorizontalAnchor = anchor,
                VerticalAnchor = VerticalAnchor.Top,
            },
        };

    private static RectNode Gap(float width)
        => new() { Style = new UIStyle { Width = width } };
}
