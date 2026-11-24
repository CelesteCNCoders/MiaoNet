using System.Globalization;
using Celeste.Mod.ChatInputBox;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class LevelClearChatItem : ChatItem
{
    private static readonly Color AccentASide = new(118, 206, 255);
    private static readonly Color AccentBSide = new(255, 134, 178);
    private static readonly Color AccentCSide = Color.Gold;
    private static readonly Color Muted = new(202, 194, 214);
    private static readonly Color BerryRed = new(255, 126, 150);

    private const float CardWidthInLines = 16f;
    private const float BaseHeightInLines = 3.45f;
    private const float WrappedHeightInLines = 4.35f;
    private const float StatTextScale = 0.95f;

    private readonly string playerDisplayName;
    private readonly Color playerColor;
    private readonly DateTime dateTime;
    private readonly LevelClearRecord record;
    private readonly MTexture? heartIcon;
    private readonly MTexture? skullIcon;
    private readonly Color skullTint;
    private readonly Color accentColor;
    private readonly List<BadgeInfo> badges;

    private readonly record struct BadgeInfo(string Label, Color Color, MTexture? Icon);

    public LevelClearChatItem(string playerDisplayName, Color playerColor, DateTime dateTime, LevelClearRecord record)
        : base(new ChatText([]))
    {
        this.playerDisplayName = Emoji.Apply(playerDisplayName);
        this.playerColor = playerColor;
        this.dateTime = dateTime;
        this.record = record;

        badges = new List<BadgeInfo>(3);
        if (record.Strawberries > 0)
        {
            badges.Add(new BadgeInfo(
                string.Create(CultureInfo.InvariantCulture, $"{record.Strawberries}/{record.TotalStrawberries}"),
                BerryRed,
                GetGuiTexture("collectables/strawberry")
            ));
        }
        if (record.FullClear)
        {
            badges.Add(new BadgeInfo(
                string.Empty,
                Color.Gold,
                GetJournalTexture("fullclear")
            ));
        }
        if (record.Golden)
        {
            badges.Add(new BadgeInfo(
                string.Empty,
                Color.Gold,
                GetGuiTexture("collectables/goldberry")
            ));
        }

        // Resolve CollabUtils2 lobby heart, skull & theme color once on construction, falling back to vanilla A/B/C.
        AreaMode areaMode = (AreaMode)Math.Clamp((int)record.Mode, 0, 2);
        MTexture? vanillaSkull = GetGuiTexture(areaMode switch
        {
            AreaMode.BSide => "collectables/skullRed",
            AreaMode.CSide => "collectables/skullGold",
            _ => "collectables/skullBlue"
        });
        if (CollabUtils2Interop.TryGetCollabCardTheme(
                record.MapSID,
                areaMode,
                out MTexture? collabHeart,
                out MTexture? collabSkull,
                out Color collabSkullTint,
                out Color? collabColor))
        {
            heartIcon = collabHeart ?? GetJournalTexture($"heartgem{(int)areaMode}");
            skullIcon = collabSkull ?? vanillaSkull;
            skullTint = collabSkull is not null ? collabSkullTint : Color.White;
            accentColor = record.Golden ? AccentCSide : (collabColor ?? GetVanillaAccent(record));
        }
        else
        {
            heartIcon = GetJournalTexture($"heartgem{(int)areaMode}");
            skullIcon = vanillaSkull;
            skullTint = Color.White;
            accentColor = GetVanillaAccent(record);
        }
    }

    public override float MeasureHeight(IScalelessTextRenderer renderer, float padding)
    {
        float line = renderer.LineHeight;
        float width = Math.Min(line * CardWidthInLines, Engine.Width - 32f);
        bool wrapBadges = ShouldWrapBadges(renderer, width, out _, out _);
        return line * (wrapBadges ? WrappedHeightInLines : BaseHeightInLines) + padding * 2f;
    }

    public override void render(
        float x,
        float y,
        float fade,
        float backgroundOpacity,
        float textOpacity,
        float messageYPadding,
        IScalelessTextRenderer textRenderer,
        bool fancyCounter,
        float counterAnimClock,
        float counterPopProgress
    )
    {
        float line = textRenderer.LineHeight;
        float width = Math.Min(line * CardWidthInLines, Engine.Width - x - 16f);
        bool wrapBadges = ShouldWrapBadges(textRenderer, width, out float badgesWidth, out _);
        float height = line * (wrapBadges ? WrappedHeightInLines : BaseHeightInLines);
        float top = y - height - messageYPadding;
        float insetX = line * 0.48f;
        float left = x + insetX;
        float right = x + width - insetX;
        float contentWidth = right - left;

        float cardOpacity = MiaoNetModule.Settings.ClearCardOpacityValue;
        float bgAlpha = fade * backgroundOpacity * cardOpacity;
        float fgAlpha = fade * textOpacity * cardOpacity;

        DrawCardBackground(x, top, width, height, accentColor, bgAlpha, fgAlpha, textRenderer.Scale);

        // Row 1: player avatar + prefix + name in player's own color, plus timestamp
        string heading = Dialog.Clean("miaonet_clear_title");
        string timeOfDay = dateTime.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        string headerRightText = $"{heading}  {timeOfDay}";
        float headerRightWidth = textRenderer.Measure(headerRightText).X * 0.68f;

        DrawText(playerDisplayName, left, top + line * 0.3f, 0.78f, playerColor, contentWidth - headerRightWidth - line * 0.4f);
        textRenderer.Draw(
            headerRightText,
            new Vector2(right, top + line * 0.34f),
            new Vector2(1f, 0f),
            Vector2.One * 0.68f,
            Muted * fgAlpha
        );

        // Row 2: heart icon (CollabUtils2 lobby heart or vanilla A/B/C heart) + map name
        string side = record.Mode switch { 1 => "B", 2 => "C", _ => "A" };
        float titleX = left;
        float titleY = top + line * 1.15f;
        if (heartIcon is not null)
        {
            float iconSize = line * 0.9f;
            DrawIcon(heartIcon, titleX + iconSize * 0.5f, titleY + line * 0.48f, iconSize, fgAlpha);
            titleX += iconSize + line * 0.24f;
        }
        DrawText($"{record.MapName} · {side}", titleX, titleY, 0.96f, accentColor, right - titleX);

        // Row 3: stopwatch + time (if speedrun clock enabled), skull + deaths, and right-aligned badges
        float statsY = top + line * 2.22f;
        float statsCenterY = statsY + line * 0.48f;
        float statsX = left;
        float iconSizeStat = line * 0.84f;
        float iconGap = line * 0.18f;
        float itemGap = line * 0.55f;

        if (record.FormatTime() is string clearTime)
        {
            MTexture? clockIcon = GetJournalTexture("time");
            if (clockIcon is not null)
            {
                DrawIcon(clockIcon, statsX + iconSizeStat * 0.5f, statsCenterY, iconSizeStat, fgAlpha);
                statsX += iconSizeStat + iconGap;
            }
            float timeWidth = textRenderer.Measure(clearTime).X * StatTextScale;
            DrawText(clearTime, statsX, statsY, StatTextScale, Color.White, contentWidth);
            statsX += timeWidth + itemGap;
        }

        if (skullIcon is not null)
        {
            DrawIcon(skullIcon, statsX + iconSizeStat * 0.5f, statsCenterY, iconSizeStat, fgAlpha, skullTint);
            statsX += iconSizeStat + iconGap;
        }
        string deathText = string.Create(CultureInfo.InvariantCulture, $"{record.Deaths}");
        DrawText(deathText, statsX, statsY, StatTextScale, Color.White, right - statsX);

        if (badges.Count > 0)
        {
            float badgeX = wrapBadges ? left : right - badgesWidth;
            float badgeY = wrapBadges ? top + line * 3.16f : statsY;
            float badgeCenterY = badgeY + line * 0.48f;

            foreach (var badge in badges)
            {
                bool hasLabel = !string.IsNullOrEmpty(badge.Label);
                if (badge.Icon is not null)
                {
                    DrawIcon(badge.Icon, badgeX + iconSizeStat * 0.5f, badgeCenterY, iconSizeStat, fgAlpha);
                    badgeX += iconSizeStat + (hasLabel ? iconGap : 0f);
                }
                if (hasLabel)
                {
                    float labelWidth = textRenderer.Measure(badge.Label).X * StatTextScale;
                    DrawText(badge.Label, badgeX, badgeY, StatTextScale, badge.Color, right - badgeX);
                    badgeX += labelWidth;
                }
                badgeX += itemGap;
            }
        }

        void DrawText(string text, float dx, float dy, float scale, Color color, float maxWidth)
        {
            if (maxWidth <= 0f)
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
            textRenderer.Draw(text, new Vector2(dx, dy), Vector2.Zero, Vector2.One * scale, color * fgAlpha);
        }
    }

    private bool ShouldWrapBadges(IScalelessTextRenderer textRenderer, float cardWidth, out float badgesWidth, out float leftStatsWidth)
    {
        float line = textRenderer.LineHeight;
        float insetX = line * 0.48f;
        float contentWidth = cardWidth - insetX * 2f;
        float iconSize = line * 0.84f;
        float iconGap = line * 0.18f;
        float itemGap = line * 0.55f;

        leftStatsWidth = 0f;
        if (record.FormatTime() is string clearTime)
        {
            if (GetJournalTexture("time") is not null)
                leftStatsWidth += iconSize + iconGap;
            leftStatsWidth += textRenderer.Measure(clearTime).X * StatTextScale + itemGap;
        }
        leftStatsWidth += iconSize + iconGap;
        leftStatsWidth += textRenderer.Measure(string.Create(CultureInfo.InvariantCulture, $"{record.Deaths}")).X * StatTextScale;

        badgesWidth = 0f;
        for (int i = 0; i < badges.Count; i++)
        {
            var badge = badges[i];
            bool hasLabel = !string.IsNullOrEmpty(badge.Label);
            if (badge.Icon is not null)
                badgesWidth += iconSize + (hasLabel ? iconGap : 0f);
            if (hasLabel)
                badgesWidth += textRenderer.Measure(badge.Label).X * StatTextScale;
            if (i + 1 < badges.Count)
                badgesWidth += itemGap;
        }

        return badges.Count > 0 && leftStatsWidth + itemGap + badgesWidth > contentWidth;
    }

    private static void DrawCardBackground(
        float x,
        float top,
        float width,
        float height,
        Color accent,
        float bgAlpha,
        float fgAlpha,
        float uiScale
    )
    {
        Draw.Rect(x, top, width, height, new Color(28, 20, 36) * bgAlpha);
        Draw.HollowRect(x, top, width, height, accent * fgAlpha * 0.55f);
        Draw.Rect(x, top, Math.Max(2f, uiScale * 2f), height, accent * fgAlpha);
    }

    private static Color GetVanillaAccent(LevelClearRecord record)
        => record.Golden || record.Mode == 2
            ? AccentCSide
            : record.Mode == 1
                ? AccentBSide
                : AccentASide;

    private static void DrawIcon(MTexture icon, float centerX, float centerY, float targetSize, float alpha, Color? tint = null)
    {
        float maxDim = Math.Max(icon.ClipRect.Width, icon.ClipRect.Height);
        if (maxDim <= 0f)
            return;
        float scale = targetSize / maxDim;
        icon.DrawCentered(new Vector2(centerX, centerY), (tint ?? Color.White) * alpha, scale);
    }

    private static MTexture? GetGuiTexture(string path)
        => GFX.Gui is not null && GFX.Gui.Has(path) ? GFX.Gui[path] : null;

    private static MTexture? GetJournalTexture(string path)
        => MTN.Loaded && MTN.Journal is not null && MTN.Journal.Has(path) ? MTN.Journal[path] : null;
}
