// 在通关结算后保存本次成绩。
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
        // 不能用 Strawberries.Count：其中包含金草莓、月莓等额外实体。
        var berries = map.Strawberries.Where(e => !e.Bool("moon")).Select(GetID).Distinct().ToArray();
        int collected = berries.Count(session.Strawberries.Contains);
        bool fullClear = session.StartedFromBeginning && collected == berries.Length
            && (!map.DetectedCassette || session.Cassette)
            && (!map.DetectedHeartGem || session.HeartGem)
            && (session.Area.SID != "Celeste/7-Summit" || session.Area.Mode != AreaMode.Normal || session.HasAllSummitGems);
        bool golden = map.Goldenberries.Concat(map.DashlessGoldenberries ?? [])
            .Select(GetID).Any(session.Strawberries.Contains);
        string name = Dialog.Clean(AreaData.Get(session).Name);
        var record = new LevelClearRecord
        {
            MapSID = CleanText(session.Area.SID, LevelClearRecord.MaxMapSIDLength),
            MapName = CleanText(name, LevelClearRecord.MaxMapNameLength),
            Mode = (byte)session.Area.Mode,
            TimeTicks = session.Time,
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
        // 避免截断 UTF-16 代理对。
        return text[..(char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit)];
    }
}
