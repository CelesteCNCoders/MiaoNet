using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public static class ChatChannelMatcher
{
    public static readonly string[] Names = ["global", "channel", "map"];

    public static ChatChannel Match(string name)
    {
        StringComparison sc = StringComparison.CurrentCultureIgnoreCase;

        if (name.Equals("global", sc) || name.Equals("g", sc))
            return ChatChannel.Global;

        if (name.Equals("channel", sc) || name.Equals("c", sc))
            return ChatChannel.Channel;

        if (name.Equals("map", sc) || name.Equals("m", sc))
            return ChatChannel.Map;

        return (ChatChannel)(-1);
    }

    // Use this method for ChatTab localization
    public static ChatChannel MatchLocalized(string name)
    {
        StringComparison sc = StringComparison.CurrentCultureIgnoreCase;
        if (name.Equals(Dialog.Get("miaonet_chat_channel_name_global"), sc))
            return ChatChannel.Global;
        
        if (name.Equals(Dialog.Get("miaonet_chat_channel_name_channel"), sc))
            return ChatChannel.Channel;

        if (name.Equals(Dialog.Get("miaonet_chat_channel_name_map"), sc))
            return ChatChannel.Map;
        
        return (ChatChannel)(-1);
    }

    public static string? GetName(ChatChannel? channel) =>
        channel switch
        {
            ChatChannel.Global => "Global",
            ChatChannel.Channel => "Channel",
            ChatChannel.Map => "Map",
            null => null,
        };

    public static string? GetLocalizedName(ChatChannel? channel) =>
        channel switch
        {
            ChatChannel.Global => Dialog.Get("miaonet_chat_channel_name_global"),
            ChatChannel.Channel => Dialog.Get("miaonet_chat_channel_name_channel"),
            ChatChannel.Map => Dialog.Get("miaonet_chat_channel_name_map"),
            null => null,
        };

    // TODO: Private Chat Matching
}
