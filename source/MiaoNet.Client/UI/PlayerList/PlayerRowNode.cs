using System;
using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Layout;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.PlayerList;

// one player row: zebra stripe, name with status icons, then a right-aligned location group and a
// fixed-width ping column.
//
// positions come entirely from the flex layout: the name group sits at the start, a spacer pushes
// the location group and ping to the end, and the ping column is a fixed width so pings line up
// across rows. no hand-computed coordinates anywhere.
public sealed class PlayerRowNode : BoxNode
{
    private readonly PlayerListMetrics metrics;
    private readonly PlayerListIcons icons;
    private readonly Color stripe;
    private IconNode? pausedIcon;

    public PlayerRowNode(PlayerRow row, PlayerListMetrics metrics, PlayerListIcons icons, bool evenRow)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(icons);

        this.metrics = metrics;
        this.icons = icons;
        stripe = evenRow ? MiaoNetUITheme.PlayerList.StripeEven : MiaoNetUITheme.PlayerList.StripeOdd;

        Style = new UIStyle
        {
            Width = metrics.RowWidth,
            Padding = new EdgeInsets(PlayerListLayout.RowPaddingX, PlayerListLayout.RowPaddingY),
        };

        Child = BuildContent(row);
    }

    // apply the floating paused-icon animation
    public void SetPausedIconOffset(float offsetX)
    {
        if (pausedIcon is not null)
        {
            pausedIcon.PaintOffset = new Vector2(offsetX, 0f);
        }
    }

    protected override void PaintSelf(IUICanvas canvas, float opacity)
    {
        canvas.FillRect(Bounds, stripe * opacity);
        base.PaintSelf(canvas, opacity);
    }

    // a horizontal flex; the content row also pins the height to the line height
    private static FlexNode Row(float? height = null)
        => new()
        {
            Axis = FlexAxis.Horizontal,
            Spacing = 0f,
            CrossAxisAlignment = CrossAxisAlignment.Start,
            Style = new UIStyle { Height = height },
        };

    private FlexNode BuildContent(PlayerRow row)
    {
        FlexNode content = Row(metrics.LineHeight);
        content.Add(BuildNameGroup(row));
        content.Add(new SpacerNode());
        content.Add(BuildLocationGroup(row));
        content.Add(BuildPingColumn(row));
        return content;
    }

    private FlexNode BuildNameGroup(PlayerRow row)
    {
        FlexNode group = Row();
        group.Add(Text(row.DisplayName, row.NameColor));

        foreach (PlayerStatus status in PlayerListMetrics.StatusOrder)
        {
            if (!row.Status.HasFlag(status) || icons.For(status) is not { } texture)
            {
                continue;
            }

            // the floating paused icon gets a gap on both sides, the others none
            if (status == PlayerStatus.Paused)
            {
                group.Add(Gap(PlayerListLayout.PausedGap));
            }

            IconNode icon = Icon(texture);
            group.Add(icon);

            if (status == PlayerStatus.Paused)
            {
                pausedIcon = icon;
                group.Add(Gap(PlayerListLayout.PausedGap));
            }
        }

        return group;
    }

    private FlexNode BuildLocationGroup(PlayerRow row)
    {
        FlexNode group = Row();
        if (!row.HasLocation)
        {
            return group;
        }

        if (row.UsesDebugRoomIcon && icons.DebugMap is { } debugMap)
        {
            group.Add(Icon(debugMap));
        }
        else if (row.RoomText is { Length: > 0 } room)
        {
            group.Add(Text(room, MiaoNetUITheme.PlayerList.Room));
        }

        group.Add(Text(":", MiaoNetUITheme.PlayerList.Room));
        group.Add(Gap(metrics.SpaceWidth));

        if (row.MapName is { Length: > 0 } mapName)
        {
            group.Add(Text(mapName, row.MapNameColor));
        }

        if (row.AreaModeText is { Length: > 0 } areaMode)
        {
            group.Add(Gap(metrics.SpaceWidth));
            group.Add(Text(areaMode, row.MapSideColor));
        }

        if (row.AreaIcon is { } areaIcon)
        {
            group.Add(Gap(metrics.SpaceWidth));
            group.Add(Icon(areaIcon));
        }

        return group;
    }

    private BoxNode BuildPingColumn(PlayerRow row)
    {
        var column = new BoxNode { Style = new UIStyle { Width = metrics.PingColumnWidth } };
        if (row.PingText is { Length: > 0 } ping)
        {
            column.Child = Text(ping, MiaoNetUITheme.PlayerList.Ping, HorizontalAnchor.Right);
        }

        return column;
    }

    // the colour lives in the text style; the node style only supplies the renderer
    private TextNode Text(string text, Color color, HorizontalAnchor anchor = HorizontalAnchor.Left)
        => new()
        {
            Text = text,
            Style = new UIStyle { TextRenderer = metrics.Renderer },
            TextStyle = metrics.TextStyle with
            {
                Color = color,
                HorizontalAnchor = anchor,
                VerticalAnchor = VerticalAnchor.Top,
            },
        };

    private IconNode Icon(IUITexture texture)
        => new()
        {
            Texture = texture,
            TargetHeight = metrics.LineHeight,
            Tint = MiaoNetUITheme.PlayerList.Icon,
        };

    private static RectNode Gap(float width)
        => new() { Style = new UIStyle { Width = width } };
}
