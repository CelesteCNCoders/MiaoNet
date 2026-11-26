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
            flags: ChatChannelFlags.None
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
    }

    [TestMethod]
    public void PacketChatMessageSupportsChannelAndExternalNullSender()
    {
        var timestamp = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var original = new PacketChatMessage(timestamp, ChatMessageType.Channel, chatChannelID: 5, sourcePlayer: null, content: "hello from mc");

        byte[] bytes = RefBinarySerialization.Serialize(original);
        var restored = RefBinarySerialization.Deserialize<PacketChatMessage>(bytes);

        Assert.AreEqual(timestamp, restored.DateTime);
        Assert.AreEqual(ChatMessageType.Channel, restored.Type);
        Assert.AreEqual(5, restored.ChatChannelID);
        Assert.IsNull(restored.SourcePlayer);
        Assert.AreEqual("hello from mc", restored.Content);
    }

    [TestMethod]
    public void PacketClientInitialRoundTripsChatChannels()
    {
        ChatChannelInfo[] chatChannels =
        [
            new(0, "public", "Public", string.Empty, Color.White, ChatChannelFlags.CanSend),
            new(3, "announcements", "Announcements", string.Empty, new Color(255, 215, 0), ChatChannelFlags.None)
        ];
        var original = new PacketClientInitial(
            channelID: 0,
            playerID: 42,
            new PlayerInfo(1, "Madeline", string.Empty, string.Empty, Color.White),
            Array.Empty<PacketClientInitial.Channel>(),
            chatChannels,
            Array.Empty<PacketClientInitial.Player>(),
            new PlayerPresenceMessage("join", "leave"),
            "welcome"
        );

        byte[] bytes = RefBinarySerialization.Serialize(original);
        var restored = RefBinarySerialization.Deserialize<PacketClientInitial>(bytes);

        Assert.HasCount(2, restored.ChatChannels);
        var list = restored.ChatChannels.ToList();
        Assert.AreEqual("public", list[0].Key);
        Assert.IsTrue(list[0].CanSend);
        Assert.AreEqual("announcements", list[1].Key);
        Assert.IsFalse(list[1].CanSend);
    }
}
