// 通关卡片直接绘制到聊天列表，布局随聊天字号缩放。
using System.Globalization;
using Celeste.Mod.ChatInputBox;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class LevelClearChatItem(string playerName, DateTime dateTime, LevelClearRecord record)
    : ChatItem(new ChatText([]))
{
    private static readonly Color Pink = new(255, 182, 200);
    private static readonly Color Muted = new(186, 174, 194);
    private static readonly Color BerryRed = new(255, 112, 136);

    private bool HasStamps => record.Strawberries > 0 || record.Golden;

    public override float MeasureHeight(IScalelessTextRenderer renderer, float padding)
        => MiaoNetModule.Settings.ClearCardView switch
        {
            ClearCardView.Hidden => 0,
            ClearCardView.Compact => renderer.LineHeight * (HasStamps ? 3.1f : 2.1f) + padding * 2,
            _ => renderer.LineHeight * (HasStamps ? 4.9f : 3.5f) + padding * 2
        };

    public override void render(float x, float y, float fade, float backgroundOpacity, float textOpacity,
        float messageYPadding, IScalelessTextRenderer textRenderer,
        bool fancyCounter, float counterAnimClock, float counterPopProgress)
    {
        if (MiaoNetModule.Settings.ClearCardView == ClearCardView.Hidden)
            return;
        bool compact = MiaoNetModule.Settings.ClearCardView == ClearCardView.Compact;
        float line = textRenderer.LineHeight;
        float width = Math.Min(line * (compact ? 17 : 20), Engine.Width - x - 16);
        float height = MeasureHeight(textRenderer, messageYPadding) - messageYPadding * 2;
        float top = y - height - messageYPadding;
        float inset = line * 0.4f;
        float left = x + inset;
        float contentWidth = width - inset * 2;
        float alpha = fade * textOpacity;
        Color accent = record.Golden ? Color.Gold : Pink;

        Draw.Rect(x, top, width, height, new Color(28, 20, 36) * fade * backgroundOpacity);
        Draw.HollowRect(x, top, width, height, accent * alpha * 0.55f);
        Draw.Rect(x, top, Math.Max(2, textRenderer.Scale * 2), height, accent * alpha);

        string side = record.Mode switch { 1 => "B", 2 => "C", _ => "A" };
        string deaths = PFormat.Format(CultureInfo.InvariantCulture, Dialog.Clean("miaonet_clear_deaths"), record.Deaths);
        if (compact)
        {
            DrawText(record.MapName + " · " + side, left, top + line * 0.2f, 0.85f, accent, contentWidth * 0.65f);
            DrawText(playerName, left + contentWidth * 0.69f, top + line * 0.3f, 0.65f, Muted, contentWidth * 0.31f);
            DrawText(record.FormatTime(), left, top + line * 1.02f, 0.95f, Color.White, contentWidth * 0.55f);
            DrawText(deaths, left + contentWidth * 0.6f, top + line * 1.15f, 0.7f, Pink, contentWidth * 0.4f);
        }
        else
        {
            string heading = Dialog.Clean("miaonet_clear_title");
            string time = dateTime.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            float headerRight = textRenderer.Measure(heading + "  " + time).X * 0.7f;
            DrawText(playerName, left, top + line * 0.25f, 0.75f, Muted, contentWidth - headerRight - inset);
            textRenderer.Draw(heading + "  " + time, new(x + width - inset, top + line * 0.25f),
                new(1, 0), Vector2.One * 0.7f, Muted * alpha);
            DrawText(record.MapName + " · " + side, left, top + line * 1.05f, 1f, accent, contentWidth);
            DrawText(record.FormatTime(), left, top + line * 2.12f, 1.2f, Color.White, contentWidth * 0.55f);
            DrawText(deaths, left + contentWidth * 0.6f, top + line * 2.35f, 0.8f, Pink, contentWidth * 0.4f);
        }

        float stampX = left;
        if (record.Strawberries > 0)
            Stamp(string.Create(CultureInfo.InvariantCulture, $"{record.Strawberries}/{record.TotalStrawberries}"),
                BerryRed, GFX.Game.GetAtlasSubtextures("collectables/strawberry/normal").FirstOrDefault());
        if (record.Golden)
            Stamp(Dialog.Clean("miaonet_clear_golden_stamp"), Color.Gold,
                GFX.Game.GetAtlasSubtextures("collectables/goldberry/idle").FirstOrDefault());

        void Stamp(string label, Color color, MTexture? icon = null)
        {
            float by = top + line * (compact ? 2.05f : 3.7f);
            float iconWidth = icon is null ? 0 : line * 0.9f;
            float stampWidth = textRenderer.Measure(label).X * 0.65f + iconWidth + line * 0.5f;
            Draw.Rect(stampX, by, stampWidth, line * 0.9f, color * alpha * 0.15f);
            Draw.HollowRect(stampX, by, stampWidth, line * 0.9f, color * alpha);
            if (icon is not null)
                icon.DrawCentered(new(stampX + line * 0.2f + iconWidth * 0.5f, by + line * 0.45f),
                    Color.White * alpha, line * 0.75f / Math.Max(icon.Width, icon.Height));
            DrawText(label, stampX + line * 0.25f + iconWidth, by + line * 0.12f, 0.65f, color, stampWidth - iconWidth - line * 0.5f);
            stampX += stampWidth + line * 0.25f;
        }

        void DrawText(string text, float dx, float dy, float scale, Color color, float maxWidth)
        {
            if (maxWidth <= 0)
                return;
            if (textRenderer.Measure(text).X * scale > maxWidth)
            {
                const string suffix = "...";
                int length = text.Length;
                while (length > 0 && textRenderer.Measure(text[..length] + suffix).X * scale > maxWidth)
                {
                    length--;
                    if (length > 0 && char.IsHighSurrogate(text[length - 1]))
                        length--;
                }
                text = text[..length] + suffix;
            }
            textRenderer.Draw(text, new(dx, dy), Vector2.Zero, Vector2.One * scale, color * alpha);
        }
    }
}
