using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.Controls;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Layout;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.PlayerList;

// root of the player list: a scrolling column of channel sections. the host gives it the
// screen-sized rectangle; one channel margin is folded into the panel padding, and the rest of
// the spacing between channels comes from the column.
public sealed class PlayerListPanelNode : ScrollNode
{
    private readonly ITextRenderer renderer;
    private readonly List<PlayerRowNode> rowNodes = [];

    public PlayerListPanelNode(ITextRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        this.renderer = renderer;
    }

    // widths from the latest rebuild, null before the first one
    public PlayerListMetrics? Metrics { get; private set; }

    // rebuild the channel sections; call when the player list data changes
    public void Rebuild(IReadOnlyList<PlayerListChannel> channels, PlayerListIcons icons, float scale, float lineHeight)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(icons);

        PlayerListMetrics metrics = PlayerListMetrics.Compute(channels, renderer, scale, lineHeight, icons);
        Metrics = metrics;

        rowNodes.Clear();
        var column = new FlexNode
        {
            Axis = FlexAxis.Vertical,
            Spacing = PlayerListLayout.ChannelSpacing,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
            Style = new UiStyle { Width = metrics.PanelWidth, TextRenderer = renderer },
        };

        foreach (PlayerListChannel channel in channels)
        {
            var channelNode = new PlayerListChannelNode(channel, renderer, metrics, icons, scale);
            column.Add(channelNode);
            rowNodes.AddRange(channelNode.RowNodes);
        }

        // the panel's own margin is folded in here: 16 left, 16 above the first and 16 below the
        // last channel. no right margin on purpose: the width comes from the widest row instead.
        Child = new BoxNode
        {
            Style = new UiStyle
            {
                Padding = new EdgeInsets(
                    PlayerListLayout.PanelMarginX,
                    PlayerListLayout.PanelMarginY,
                    0f,
                    PlayerListLayout.PanelMarginY),
                TextRenderer = renderer,
            },
            Child = column,
        };

        InvalidateMeasure();
    }

    // hand the floating paused-icon offset down to every row
    public void SetPausedIconOffset(float offsetX)
    {
        foreach (PlayerRowNode row in rowNodes)
        {
            row.SetPausedIconOffset(offsetX);
        }
    }
}
