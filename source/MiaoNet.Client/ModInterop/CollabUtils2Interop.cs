#pragma warning disable CA2211

using System.Reflection;
using MonoMod.ModInterop;

namespace Celeste.Mod.MiaoNet;

[ModImportName("CollabUtils2.LobbyHelper")]
public static class CollabUtils2Interop
{
    public static Func<string, string>? GetLobbyForMap;
    public static Func<string, bool>? IsCollabMap;

    private static readonly (string Keyword, string MiniHeartPath, Color AccentColor, string? FallbackSkullPath, Color SkullTint)[] StandardLobbyTiers =
    [
        ("beginner",     "CollabUtils2/miniHeart/bgr/000", Calc.HexToColor("56b3ff"), "collectables/skullBlue", Color.White),
        ("intermediate", "CollabUtils2/miniHeart/imd/000", Calc.HexToColor("ff6d81"), "collectables/skullRed",  Color.White),
        ("advanced",     "CollabUtils2/miniHeart/adv/000", Calc.HexToColor("ffff89"), "collectables/skullGold", Color.White),
        ("expert",       "CollabUtils2/miniHeart/exp/000", Calc.HexToColor("ff9e66"), "collectables/skullBlue", Calc.HexToColor("ff9e66")),
        ("grandmaster",  "CollabUtils2/miniHeart/gdm/000", Calc.HexToColor("dd87ff"), "collectables/skullBlue", Calc.HexToColor("dd87ff"))
    ];

    private static bool endScreenTypeResolved;
    private static Type? areaCompleteInfoInLevelType;

    private static bool heartLookupResolved;
    private static MethodInfo? getGuiHeartSpriteIdMethod;
    private static MethodInfo? getDeathsIconMethod;
    private static FieldInfo? heartSpriteBankField;

    private static EverestModule? FindCollabUtils2Module()
        => Everest.Modules.FirstOrDefault(m =>
            string.Equals(m.Metadata?.Name, "CollabUtils2", StringComparison.Ordinal));

    public static bool TryEnableEndScreenForAllMaps()
    {
        EverestModule? module = FindCollabUtils2Module();
        if (module is null)
            return false;

        try
        {
            object? settings = module._Settings;
            PropertyInfo? prop = settings?.GetType().GetProperty(
                "DisplayEndScreenForAllMaps",
                BindingFlags.Public | BindingFlags.Instance
            );
            if (prop is null || !prop.CanWrite || prop.PropertyType != typeof(bool))
                return false;

            if (prop.GetValue(settings) is not true)
            {
                prop.SetValue(settings, true);
                module.SaveSettings();
                Logger.Info(LT.MiaoNet, "Enabled CollabUtils2 'DisplayEndScreenForAllMaps' on first load.");
            }
            return true;
        }
        catch (Exception e)
        {
            Logger.Warn(LT.MiaoNet, "Failed to auto-enable CollabUtils2 DisplayEndScreenForAllMaps.");
            Logger.LogDetailed(e, LT.MiaoNet);
            return false;
        }
    }

    public static bool HasInLevelEndScreen(Scene? scene)
    {
        if (scene is not Level level)
            return false;

        if (!endScreenTypeResolved)
        {
            endScreenTypeResolved = true;
            areaCompleteInfoInLevelType = FindCollabUtils2Module()?.GetType().Assembly
                .GetType("Celeste.Mod.CollabUtils2.UI.AreaCompleteInfoInLevel");
        }

        if (areaCompleteInfoInLevelType is null)
            return false;

        foreach (Entity entity in level.Entities)
        {
            if (areaCompleteInfoInLevelType.IsInstanceOfType(entity))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves CollabUtils2 lobby heart icon, death skull icon, and theme color for a collab map SID, if available.
    /// Falls back gracefully when CollabUtils2 or the specific collab map is not installed.
    /// </summary>
    public static bool TryGetCollabCardTheme(
        string mapSID,
        AreaMode mode,
        out MTexture? heartIcon,
        out MTexture? skullIcon,
        out Color skullTint,
        out Color? accentColor
    )
    {
        heartIcon = null;
        skullIcon = null;
        skullTint = Color.White;
        accentColor = null;

        if (string.IsNullOrEmpty(mapSID) || FindCollabUtils2Module() is null)
            return false;

        EnsureHelperReflection();

        string? lobbySID = GetLobbyForMap?.Invoke(mapSID);
        bool isCollab = !string.IsNullOrEmpty(lobbySID) || (IsCollabMap?.Invoke(mapSID) ?? false);

        // 1. Prefer CollabUtils2's own registered GUI heart and death skull sprites (covers per-collab art).
        heartIcon = TryGetCustomGuiHeartTexture(mapSID, mode);
        skullIcon = TryGetCustomDeathsIconTexture(mapSID, mode);

        // 2. Match standard 5-tier lobby theme (Beginner/Intermediate/Advanced/Expert/Grandmaster)
        //    from either the resolved lobby SID or the map SID itself.
        string tierProbe = lobbySID ?? mapSID;
        foreach (var (keyword, miniHeartPath, color, fallbackSkullPath, fallbackSkullTint) in StandardLobbyTiers)
        {
            if (tierProbe.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                accentColor = color;
                if (heartIcon is null && GFX.Gui is not null && GFX.Gui.Has(miniHeartPath))
                    heartIcon = GFX.Gui[miniHeartPath];
                if (skullIcon is null && fallbackSkullPath is not null && GFX.Gui is not null && GFX.Gui.Has(fallbackSkullPath))
                {
                    skullIcon = GFX.Gui[fallbackSkullPath];
                    skullTint = fallbackSkullTint;
                }
                return true;
            }
        }

        // 3. For custom-named collab lobbies, fall back to the map or lobby's TitleBaseColor.
        if (isCollab)
        {
            AreaData? area = AreaData.Get(mapSID) ?? (lobbySID is not null ? AreaData.Get(lobbySID) : null);
            if (area is not null)
                accentColor = area.TitleBaseColor;
            return heartIcon is not null || skullIcon is not null || accentColor is not null;
        }

        return heartIcon is not null || skullIcon is not null;
    }

    private static void EnsureHelperReflection()
    {
        if (heartLookupResolved)
            return;

        heartLookupResolved = true;
        Type? helperType = FindCollabUtils2Module()?.GetType().Assembly
            .GetType("Celeste.Mod.CollabUtils2.UI.InGameOverworldHelper");
        if (helperType is null)
            return;

        getGuiHeartSpriteIdMethod = helperType.GetMethod(
            "GetGuiHeartSpriteId",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(string), typeof(AreaMode)]
        );
        getDeathsIconMethod = helperType.GetMethod(
            "GetDeathsIcon",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(string), typeof(AreaMode)]
        );
        heartSpriteBankField = helperType.GetField(
            "HeartSpriteBank",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
        );
    }

    private static MTexture? TryGetCustomGuiHeartTexture(string mapSID, AreaMode mode)
    {
        if (getGuiHeartSpriteIdMethod is null || heartSpriteBankField is null)
            return null;

        try
        {
            if (getGuiHeartSpriteIdMethod.Invoke(null, [mapSID, mode]) is not string spriteId
                || string.IsNullOrEmpty(spriteId))
            {
                return null;
            }

            if (heartSpriteBankField.GetValue(null) is SpriteBank bank
                && bank.SpriteData.TryGetValue(spriteId, out SpriteData? data))
            {
                return data.Sprite?.Texture;
            }
        }
        catch (Exception e)
        {
            Logger.Warn(LT.MiaoNet, $"Failed to resolve CollabUtils2 heart sprite for {mapSID}.");
            Logger.LogDetailed(e, LT.MiaoNet);
        }
        return null;
    }

    private static MTexture? TryGetCustomDeathsIconTexture(string mapSID, AreaMode mode)
    {
        // InGameOverworldHelper.GetDeathsIcon dereferences AreaData.Get(mapSID) when falling back to LevelSet.
        if (getDeathsIconMethod is null || AreaData.Get(mapSID) is null)
            return null;

        try
        {
            if (getDeathsIconMethod.Invoke(null, [mapSID, mode]) is string iconPath
                && !string.IsNullOrEmpty(iconPath)
                && GFX.Gui is not null
                && GFX.Gui.Has(iconPath))
            {
                return GFX.Gui[iconPath];
            }
        }
        catch (Exception e)
        {
            Logger.Warn(LT.MiaoNet, $"Failed to resolve CollabUtils2 death skull icon for {mapSID}.");
            Logger.LogDetailed(e, LT.MiaoNet);
        }
        return null;
    }
}
