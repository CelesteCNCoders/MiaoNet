using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Layout;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.PlayerList;

// one channel section: a panel with an asymmetric border (top blue, left cyan),
// a header line and the player rows.
public sealed class PlayerListChannelNode : BoxNode
{
    public PlayerListChannelNode(
        PlayerListChannel channel,
        ITextRenderer renderer,
        PlayerListMetrics metrics,
        PlayerListIcons icons,
        float scale)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(icons);

        Style = new UiStyle
        {
            Width = metrics.PanelWidth,
            Padding = new EdgeInsets(PlayerListLayout.ChannelPaddingX, PlayerListLayout.ChannelPaddingY),
            Background = MiaoNetUiTheme.PlayerList.Background,
            TextRenderer = renderer,
        };

        var content = new FlexNode
        {
            Axis = FlexAxis.Vertical,
            Spacing = 0f,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
            Style = new UiStyle { TextRenderer = renderer },
        };

        content.Add(new TextNode
        {
            Text = channel.Header,
            Style = new UiStyle
            {
                Height = metrics.HeaderHeight,
                Foreground = MiaoNetUiTheme.PlayerList.Header,
                TextRenderer = renderer,
            },
            TextStyle = new TextStyle
            {
                Scale = scale,
                LineHeight = metrics.LineHeight,
                Color = MiaoNetUiTheme.PlayerList.Header,
                VerticalAnchor = VerticalAnchor.Top,
            },
        });

        var rows = new List<PlayerRowNode>(channel.Rows.Count);
        for (int i = 0; i < channel.Rows.Count; i++)
        {
            var row = new PlayerRowNode(channel.Rows[i], renderer, metrics, icons, scale, evenRow: i % 2 == 0);
            rows.Add(row);
            content.Add(row);
        }

        RowNodes = rows;
        Child = content;
    }

    public IReadOnlyList<PlayerRowNode> RowNodes { get; }

    protected override void PaintBorder(IUiCanvas canvas, UiRect rect, float opacity)
    {
        float thickness = PlayerListLayout.BorderThickness;
        canvas.FillRect(
            new UiRect(rect.X, rect.Y, rect.Width, thickness),
            MiaoNetUiTheme.PlayerList.BorderTop * opacity);
        canvas.FillRect(
            new UiRect(rect.X, rect.Y, thickness, rect.Height),
            MiaoNetUiTheme.PlayerList.BorderLeft * opacity);
    }
}
