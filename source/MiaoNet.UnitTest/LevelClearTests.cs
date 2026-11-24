using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class LevelClearTests
{
    [TestMethod]
    public void RecordRoundTripsWithAndWithoutTime()
    {
        var withTime = new LevelClearRecord
        {
            MapSID = "Celeste/1-ForsakenCity",
            MapName = "Forsaken City",
            Mode = 0,
            TimeTicks = TimeSpan.FromMinutes(12).Add(TimeSpan.FromSeconds(34.567)).Ticks,
            Deaths = 42,
            Strawberries = 15,
            TotalStrawberries = 20,
            FullClear = false,
            Golden = true
        };

        byte[] bytesWithTime = RefBinarySerialization.Serialize(withTime);
        var restoredWithTime = RefBinarySerialization.Deserialize<LevelClearRecord>(bytesWithTime);
        Assert.AreEqual(withTime, restoredWithTime);
        Assert.AreEqual("12:34.567", restoredWithTime.FormatTime());

        var withoutTime = withTime with { TimeTicks = null };
        byte[] bytesWithoutTime = RefBinarySerialization.Serialize(withoutTime);
        var restoredWithoutTime = RefBinarySerialization.Deserialize<LevelClearRecord>(bytesWithoutTime);
        Assert.AreEqual(withoutTime, restoredWithoutTime);
        Assert.IsNull(restoredWithoutTime.TimeTicks);
        Assert.IsNull(restoredWithoutTime.FormatTime());
        Assert.IsTrue(restoredWithoutTime.IsValid());
    }

    [TestMethod]
    public void PacketLevelClearRoundTripsWithChatChannelID()
    {
        var timestamp = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);
        var record = new LevelClearRecord
        {
            MapSID = "Collab/MapA",
            MapName = "Map A",
            Mode = 1,
            TimeTicks = null,
            Deaths = 7,
            Strawberries = 0,
            TotalStrawberries = 3
        };
        var packet = new PacketLevelClear(ServerChatChannel.AchievementsID, playerID: 9, timestamp, record);

        byte[] bytes = RefBinarySerialization.Serialize(packet);
        var restored = RefBinarySerialization.Deserialize<PacketLevelClear>(bytes);

        Assert.AreEqual(ServerChatChannel.AchievementsID, restored.ChatChannelID);
        Assert.AreEqual(9, restored.PlayerID);
        Assert.AreEqual(timestamp, restored.DateTime);
        Assert.AreEqual(record, restored.Record);
    }

    [TestMethod]
    public void RecordValidationRejectsMalformedValues()
    {
        var valid = new LevelClearRecord { MapSID = "a/b", MapName = "Map" };
        Assert.IsTrue(valid.IsValid());
        Assert.IsFalse((valid with { ClearID = "not-a-guid" }).IsValid());
        Assert.IsFalse((valid with { ClearID = Guid.Empty.ToString("N") }).IsValid());
        Assert.IsFalse((valid with { MapSID = " " }).IsValid());
        Assert.IsFalse((valid with { MapName = "line1\nline2" }).IsValid());
        Assert.IsFalse((valid with { Mode = 3 }).IsValid());
        Assert.IsFalse((valid with { TimeTicks = -1 }).IsValid());
        Assert.IsFalse((valid with { Deaths = -1 }).IsValid());
        Assert.IsFalse((valid with { Strawberries = 5, TotalStrawberries = 4 }).IsValid());
    }

    [TestMethod]
    public void ShareLimiterEnforcesCooldown()
    {
        var limiter = new LevelClearShareLimiter();
        Assert.IsTrue(limiter.TryAcquire(1_000));
        Assert.IsFalse(limiter.TryAcquire(5_000));
        Assert.IsTrue(limiter.TryAcquire(1_000 + LevelClearRecord.ShareCooldownSeconds * 1000L));
    }
}
