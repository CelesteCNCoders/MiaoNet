using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class MiaoNetModuleSaveData : EverestModuleSaveData
{
    public LevelClearRecord? LastClear { get; set; }

    public string? LastSharedClearID { get; set; }
}

internal static class LevelClearTracking
{
    public static void OnComplete(Level level)
    {
        var module = MiaoNetModule.Instance;
        if (module.IsWatching || module._SaveData is not MiaoNetModuleSaveData saveData)
            return;

        var session = level.Session;
        var map = session.MapData;

        // Exclude moon berries and non-red berries from the standard berry ratio.
        var berries = map.Strawberries.Where(e => !e.Bool("moon")).Select(GetID).Distinct().ToArray();
        int collected = berries.Count(session.Strawberries.Contains);
        var areaData = AreaData.Get(session);
        bool hasFullClearTargets = areaData.CanFullClear
            || berries.Length > 0
            || map.DetectedCassette
            || map.DetectedHeartGem;
        bool fullClear = session.Area.Mode == AreaMode.Normal
            && hasFullClearTargets
            && session.StartedFromBeginning
            && collected == berries.Length
            && (!map.DetectedCassette || session.Cassette)
            && (!map.DetectedHeartGem || session.HeartGem)
            && (session.Area.SID != "Celeste/7-Summit" || session.HasAllSummitGems);
        bool golden = map.Goldenberries.Concat(map.DashlessGoldenberries ?? [])
            .Select(GetID)
            .Any(session.Strawberries.Contains);

        // Respect the player's speedrun clock setting: omit clear time when the clock is off.
        long? timeTicks = Settings.Instance.SpeedrunClock != SpeedrunType.Off ? session.Time : null;

        string name = Dialog.Clean(areaData.Name);
        var record = new LevelClearRecord
        {
            MapSID = CleanText(session.Area.SID, LevelClearRecord.MaxMapSIDLength),
            MapName = CleanText(name, LevelClearRecord.MaxMapNameLength),
            Mode = (byte)session.Area.Mode,
            TimeTicks = timeTicks,
            Deaths = session.Deaths,
            Strawberries = collected,
            TotalStrawberries = berries.Length,
            FullClear = fullClear,
            Golden = golden
        };
        if (record.IsValid())
            saveData.LastClear = record;
    }

    private static EntityID GetID(EntityData entity) => new(entity.Level.Name, entity.ID);

    private static string CleanText(string value, int limit)
    {
        string text = new(value.Where(c => !char.IsControl(c) && c is not '\u2028' and not '\u2029').ToArray());
        if (text.Length <= limit)
            return text;
        // Avoid splitting a UTF-16 surrogate pair.
        return text[..(char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit)];
    }
}

internal static class LevelClearSharing
{
    private static long? lastClearShareTick;

    public static bool TryShareLastClear(MiaoNetContext context, out string? error)
    {
        if (!context.HasConnection || context.ClientState is null)
        {
            error = Dialog.Clean("miaonet_clear_not_connected");
            return false;
        }
        if (MiaoNetModule.Settings.LiveMode)
        {
            error = Dialog.Clean("miaonet_chat_disabled");
            return false;
        }

        var saveData = MiaoNetModule.Instance._SaveData as MiaoNetModuleSaveData;
        var record = saveData?.LastClear;
        if (record is null || !record.IsValid())
        {
            error = Dialog.Clean("miaonet_clear_no_record");
            return false;
        }
        if (saveData!.LastSharedClearID == record.ClearID)
        {
            error = Dialog.Clean("miaonet_clear_already_shared");
            return false;
        }

        long now = Environment.TickCount64;
        if (lastClearShareTick is long last && now - last < LevelClearRecord.ShareCooldownSeconds * 1000L)
        {
            error = Dialog.Clean("miaonet_clear_cooldown");
            return false;
        }

        context.QueuePacket(new PacketShareLevelClear(record));
        lastClearShareTick = now;
        error = null;
        return true;
    }
}
