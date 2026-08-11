using MiaoNet.Server;
using Microsoft.Extensions.Logging;

namespace MiaoNet.UnitTest;

[TestClass]
public class AdminBufferTest
{
    [TestMethod]
    public void TestLogBufferRingAndAfter()
    {
        AdminLogBuffer buffer = new(capacity: 4);
        for (int i = 0; i < 6; i++)
            buffer.Record(LogLevel.Information, "Test", $"msg{i}", null);

        Assert.AreEqual(5, buffer.LatestId);

        // capacity is 4: only msg2..msg5 remain
        var all = buffer.GetAfter(-1, 100);
        Assert.HasCount(4, all);
        Assert.AreEqual("msg2", all[0].Message);
        Assert.AreEqual("msg5", all[3].Message);

        // after=3: only msg4, msg5
        var newer = buffer.GetAfter(3, 100);
        Assert.HasCount(2, newer);
        Assert.AreEqual("msg4", newer[0].Message);

        // limit respected: keep the newest entries
        var limited = buffer.GetAfter(-1, 1);
        Assert.HasCount(1, limited);
        Assert.AreEqual("msg5", limited[0].Message);
    }

    [TestMethod]
    public void TestChatBufferTotalCountAndFields()
    {
        AdminChatBuffer buffer = new(capacity: 3);
        buffer.Record("global", null, "playerA", 42, "hello");
        buffer.Record("channel", "chan1", "playerB", 43, "hi");
        buffer.Record("server", null, "服务器", 0, "announce");
        buffer.Record("map", "chan1", "playerC", 44, "overflow");

        Assert.AreEqual(4, buffer.TotalCount);
        Assert.AreEqual(3, buffer.LatestId);

        var all = buffer.GetAfter(-1, 100);
        Assert.HasCount(3, all);
        Assert.AreEqual("hi", all[0].Content);
        Assert.AreEqual("chan1", all[0].ChannelName);
        Assert.AreEqual(43, all[0].AuthID);
        Assert.AreEqual("server", all[1].Type);
        Assert.AreEqual("overflow", all[2].Content);

        Assert.IsEmpty(buffer.GetAfter(3, 100));
    }
}
