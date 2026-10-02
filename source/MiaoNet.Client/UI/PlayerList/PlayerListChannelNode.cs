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
    public PlayerListChannelNode(PlayerListChannel channel, PlayerListMetrics metrics, PlayerListIcons icons)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(icons);

        Style = new UIStyle
        {
            Width = metrics.PanelWidth,
            Padding = new EdgeInsets(PlayerListLayout.ChannelPaddingX, PlayerListLayout.ChannelPaddingY),
            Background = MiaoNetUITheme.PlayerList.Background,
        };

        var content = new FlexNode
        {
            Axis = FlexAxis.Vertical,
            Spacing = 0f,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
        };

        content.Add(new TextNode
        {
            Text = channel.Header,
            Style = new UIStyle { Height = metrics.HeaderHeight, TextRenderer = metrics.Renderer },
            TextStyle = metrics.TextStyle with
            {
                Color = MiaoNetUITheme.PlayerList.Header,
                VerticalAnchor = VerticalAnchor.Top,
            },
        });

        var rows = new List<PlayerRowNode>(channel.Rows.Count);
        for (int i = 0; i < channel.Rows.Count; i++)
        {
            var row = new PlayerRowNode(channel.Rows[i], metrics, icons, evenRow: i % 2 == 0);
            rows.Add(row);
            content.Add(row);
        }

        RowNodes = rows;
        Child = content;
    }

    public IReadOnlyList<PlayerRowNode> RowNodes { get; }

    protected override void PaintBorder(IUICanvas canvas, UIRect rect, float opacity)
    {
        float thickness = PlayerListLayout.BorderThickness;
        canvas.FillRect(
            new UIRect(rect.X, rect.Y, rect.Width, thickness),
            MiaoNetUITheme.PlayerList.BorderTop * opacity);
        canvas.FillRect(
            new UIRect(rect.X, rect.Y, thickness, rect.Height),
            MiaoNetUITheme.PlayerList.BorderLeft * opacity);
    }
}
