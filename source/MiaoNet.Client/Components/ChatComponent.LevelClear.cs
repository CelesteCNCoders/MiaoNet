// 将通关消息加入全局标签，并裁剪可变高度聊天内容。
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.MiaoNet;

public sealed partial class ChatComponent
{
    private static readonly RasterizerState ChatClipRasterizer = new() { CullMode = CullMode.None, ScissorTestEnable = true };
    private readonly HashSet<(int PlayerID, string ClearID)> receivedClears = new();

    private void Context_LevelClearReceived(OnlinePlayer player, PacketLevelClear packet)
    {
        if (MiaoNetModule.Settings.LiveMode)
            return;
        if (!receivedClears.Add((player.ID, packet.Record.ClearID)))
            return;
        chatMessageBox.AddChatItem(new LevelClearChatItem(player.Info.Name, packet.DateTime, packet.Record),
            packet.DateTime, ChatChannelMatcher.GetLocalizedName(ChatChannel.Global));
    }

    private static void RenderChatClipped(Rectangle bounds, Action render)
    {
        var device = Engine.Graphics.GraphicsDevice;
        var topLeft = Vector2.Transform(new Vector2(bounds.Left, bounds.Top), Engine.ScreenMatrix);
        var bottomRight = Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), Engine.ScreenMatrix);
        var clip = Rectangle.Intersect(device.Viewport.Bounds, new Rectangle((int)topLeft.X, (int)topLeft.Y,
            Math.Max(0, (int)(bottomRight.X - topLeft.X)), Math.Max(0, (int)(bottomRight.Y - topLeft.Y))));
        if (clip.Width == 0 || clip.Height == 0)
            return;

        MiaoNetContext.EndRender();
        var previous = device.ScissorRectangle;
        device.ScissorRectangle = clip;
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
            DepthStencilState.None, ChatClipRasterizer, null, Engine.ScreenMatrix);
        try
        {
            render();
        }
        finally
        {
            Draw.SpriteBatch.End();
            device.ScissorRectangle = previous;
            MiaoNetContext.BeginRender();
        }
    }
}
