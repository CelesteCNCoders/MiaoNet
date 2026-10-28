// 验证通关消息边界、协议往返和服务端节流。
using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class LevelClearTests
{
    private static LevelClearRecord Record => new()
    {
        ClearID = "c6ef51477a9540678fca1073d379c04d",
        MapSID = "Celeste/1-ForsakenCity", MapName = "废弃城市", Mode = 1,
        TimeTicks = TimeSpan.FromMilliseconds(340633).Ticks, Deaths = 12,
        Strawberries = 20, TotalStrawberries = 20, FullClear = true, Golden = true
    };

    [TestMethod]
    public void ShareAndBroadcastRoundTripPreserveAllFields()
    {
        using var stream = new MemoryStream();
        var writer = new RefBinaryWriter(stream);
        new PacketShareLevelClear(Record).Serialize(ref writer);
        var reader = new RefBinaryReader(stream.ToArray());
        Assert.AreEqual(Record, PacketShareLevelClear.Deserialize(ref reader).Record);

        stream.SetLength(0);
        DateTime now = new(2026, 10, 28, 13, 0, 0, DateTimeKind.Utc);
        new PacketLevelClear(42, now, Record).Serialize(ref writer);
        reader = new RefBinaryReader(stream.ToArray());
        var packet = PacketLevelClear.Deserialize(ref reader);
        Assert.AreEqual(42, packet.PlayerID);
        Assert.AreEqual(now, packet.DateTime);
        Assert.AreEqual(Record, packet.Record);
        Assert.IsTrue(packet.Record.IsValid());
    }

    [TestMethod]
    public void InvalidRecordsAreRejected()
    {
        LevelClearRecord[] invalid =
        [
            Record with { ClearID = "" }, Record with { ClearID = new string('0', 32) },
            Record with { ClearID = "invalid" },
            Record with { MapSID = "" }, Record with { MapName = "   " },
            Record with { MapName = "bad\nname" }, Record with { MapSID = "bad\0sid" },
            Record with { MapName = "bad\u2028name" },
            Record with { MapName = new string('x', LevelClearRecord.MaxMapNameLength + 1) },
            Record with { MapSID = new string('x', LevelClearRecord.MaxMapSIDLength + 1) },
            Record with { Mode = 3 }, Record with { Deaths = -1 },
            Record with { Deaths = 10_000_001 }, Record with { TimeTicks = -1 },
            Record with { TimeTicks = TimeSpan.TicksPerDay * 365 + 1 },
            Record with { Strawberries = -1 }, Record with { TotalStrawberries = 19 },
            Record with { TotalStrawberries = 100_001 }
        ];
        foreach (var record in invalid)
            Assert.IsFalse(record.IsValid(), record.ToString());
    }

    [TestMethod]
    public void ZeroCollectiblesAndBoundaryValuesAreValid()
    {
        Assert.IsTrue((Record with { Strawberries = 0, TotalStrawberries = 0, Deaths = 0, TimeTicks = 0 }).IsValid());
        Assert.IsTrue((Record with
        {
            MapSID = new string('x', LevelClearRecord.MaxMapSIDLength),
            MapName = new string('中', LevelClearRecord.MaxMapNameLength), Mode = 2,
            TimeTicks = TimeSpan.TicksPerDay * 365, Deaths = 10_000_000,
            Strawberries = 100_000, TotalStrawberries = 100_000
        }).IsValid());
    }

    [TestMethod]
    [DataRow(340633L, "05:40.633")]
    [DataRow(3600000L, "1:00:00.000")]
    [DataRow(90061001L, "25:01:01.001")]
    [DataRow(0L, "00:00.000")]
    public void DurationDoesNotWrapAtOneHourOrOneDay(long milliseconds, string expected)
        => Assert.AreEqual(expected, (Record with { TimeTicks = milliseconds * TimeSpan.TicksPerMillisecond }).FormatTime());

    [TestMethod]
    public void CooldownUsesAcceptedSharesAndIsPerConnection()
    {
        var first = new LevelClearShareLimiter();
        var second = new LevelClearShareLimiter();
        Assert.IsTrue(first.TryAcquire(0));
        Assert.IsFalse(first.TryAcquire(9999));
        Assert.IsTrue(second.TryAcquire(9999));
        Assert.IsTrue(first.TryAcquire(10000));
        Assert.IsFalse(first.TryAcquire(10000));
        Assert.IsTrue(first.TryAcquire(20000));
    }

    [TestMethod]
    public void PreviousProtocolCannotConnectToCardProtocol()
    {
        Assert.IsFalse(Connection.IsVersionCompatible(new Version(0, 6, 0), Connection.Version));
        Assert.IsFalse(Connection.IsVersionCompatible(new Version(0, 5, 3), Connection.Version));
        Assert.IsFalse(Connection.IsVersionCompatible(Connection.Version, new Version(0, 5, 3)));
    }

    [TestMethod]
    public void NewClearsWithIdenticalStatsHaveDifferentIDs()
    {
        var first = new LevelClearRecord { MapSID = "test", MapName = "test" };
        var second = new LevelClearRecord { MapSID = "test", MapName = "test" };
        Assert.AreNotEqual(first.ClearID, second.ClearID);
        Assert.IsTrue(first.IsValid());
        Assert.IsTrue(second.IsValid());
    }

    [TestMethod]
    public void LegacySaveLoadByteIsIgnoredWithoutShiftingNextField()
    {
        using var stream = new MemoryStream();
        var writer = new RefBinaryWriter(stream);
        Record.Serialize(ref writer);
        byte[] bytes = stream.ToArray();
        Assert.AreEqual((byte)0, bytes[^1]);
        bytes[^1] = 1;
        var reader = new RefBinaryReader([.. bytes, 42]);
        Assert.AreEqual(Record, LevelClearRecord.Deserialize(ref reader));
        Assert.AreEqual((byte)42, reader.ReadByte());
        Assert.IsTrue(Connection.IsVersionCompatible(new Version(0, 7, 0), Connection.Version));
    }
}
