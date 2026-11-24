using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class ChatChannelSerializationTests
{
    [TestMethod]
    public void ChatChannelInfoRoundTripsThroughBinarySerialization()
    {
        var original = new ChatChannelInfo(
            id: 3,
            key: "announcements",
            displayName: "Announcements",
            prefix: "[Ann]",
            color: new Color(255, 215, 0),
            flags: ChatChannelFlags.DefaultVisibleInGlobal
        );

        byte[] bytes = RefBinarySerialization.Serialize(original);
        var restored = RefBinarySerialization.Deserialize<ChatChannelInfo>(bytes);

        Assert.AreEqual(original.ID, restored.ID);
        Assert.AreEqual(original.Key, restored.Key);
        Assert.AreEqual(original.DisplayName, restored.DisplayName);
        Assert.AreEqual(original.Prefix, restored.Prefix);
        Assert.AreEqual(original.Color.PackedValue, restored.Color.PackedValue);
        Assert.AreEqual(original.Flags, restored.Flags);
        Assert.IsFalse(restored.CanSend);
        Assert.IsTrue(restored.DefaultVisibleInGlobal);
    }
}
