using MiaoNet.Server;
using MiaoNet.Shared;
using Microsoft.Extensions.Configuration;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class ChatChannelLocalizationTests
{
    [TestMethod]
    public void ShippedChannelStringsBindIndependentlyOfAnnouncements()
    {
        var config = new ConfigurationBuilder().AddJsonFile("content.json").Build();
        var options = new ChatChannelOptions();
        config.GetRequiredSection("ChatChannels").Bind(options);

        var map = ServerChatChannel.CreateDefaultChannels().Single(c => c.Key == "map");
        var chinese = map.ToInfo(options[LanguageCode.SChinese]);
        var english = map.ToInfo(options[LanguageCode.English]);
        Assert.AreEqual("地图", chinese.DisplayName);
        Assert.AreEqual("[地图聊天]", chinese.Prefix);
        Assert.AreEqual("Map", english.DisplayName);
        Assert.AreEqual("[Map Chat]", english.Prefix);
    }

    [TestMethod]
    public void MissingChannelStringsUseDefaultsAndExplicitEmptyPrefixIsKept()
    {
        var map = ServerChatChannel.CreateDefaultChannels().Single(c => c.Key == "map");
        var options = new ChatChannelOptions();
        var untranslated = map.ToInfo(options[LanguageCode.SChinese]);
        Assert.AreEqual(map.DefaultName, untranslated.DisplayName);
        Assert.AreEqual(map.DefaultPrefix, untranslated.Prefix);

        options.SChinese[map.Key] = new() { Prefix = string.Empty };
        var localized = map.ToInfo(options[LanguageCode.SChinese]);
        Assert.AreEqual(map.DefaultName, localized.DisplayName);
        Assert.AreEqual(string.Empty, localized.Prefix);
    }
}
