using System.Collections.Immutable;
using System.Globalization;
using Celeste.Mod.ChatInputBox;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class ChatMessageFactory
{
    private static readonly Color ColorChatContent = Color.White;
    private static readonly Color ColorMapChat = Color.Cyan;
    private static readonly Color ColorChannelChat = Color.LightGray;
    private static readonly Color ColorPrivateChat = Color.LightGray;
    private static readonly Color ColorPrivateChatReceived = Color.DarkGray;
    private static readonly Color ColorMention = Color.Gold;

    private const StringComparison MentionComparison = StringComparison.OrdinalIgnoreCase;

    private readonly MiaoNetContext context;

    public ChatMessageFactory(MiaoNetContext context)
    {
        this.context = context;
    }

    public ReceivedChatMessage CreateReceived(OnlinePlayer? sender, PacketChatMessage packet)
    {
        if (packet.Type is ChatMessageType.Server or ChatMessageType.ServerChat)
            return new ReceivedChatMessage(MiaoNetChatText.CreateAnnouncement(packet.Content), false);

        string? selfName = context.ClientState?.Self.Info.Name;
        var (segments, mentionsSelf) = ParseContent(packet.Content, ContentColor(packet.Type), selfName);

        ChatText? text = packet.Type switch
        {
            ChatMessageType.Chat => CreatePublicChat(sender!, segments),
            ChatMessageType.ChannelChat => CreateChannelChat(sender!, segments),
            ChatMessageType.MapChat => CreateMapChat(sender!, segments),
            ChatMessageType.PrivateMessage => CreatePrivateChat(sender!, segments),
            _ => null
        };

        return new ReceivedChatMessage(text, mentionsSelf);
    }

    public ChatText CreateSentPrivateMessage(OnlinePlayer other, string text)
    {
        var (segments, _) = ParseContent(text, ColorPrivateChat, selfName: null);
        return CreateSentPrivateChat(other, context.ClientState!.Self, segments);
    }

    // The key and the line to show once folded, for player messages only.
    // Public chats drop the sender name since messages from different senders fold together,
    // while private messages fold per sender so their prefix and name stay accurate.
    public (string? Key, ChatText? FoldedText) CreateFoldInfo(OnlinePlayer? sender, PacketChatMessage packet)
    {
        string? selfName = context.ClientState?.Self.Info.Name;
        var (segments, _) = ParseContent(packet.Content, ContentColor(packet.Type), selfName);

        return packet.Type switch
        {
            ChatMessageType.Chat => (
                $"chat:{packet.Content}",
                new ChatText(segments)),
            ChatMessageType.ChannelChat => (
                $"channel:{packet.Content}",
                CreateFoldedTypedChat(ColorChannelChat, "miaonet_chat_channel_chat", segments)),
            ChatMessageType.MapChat => (
                $"map:{packet.Content}",
                CreateFoldedTypedChat(ColorMapChat, "miaonet_chat_map_chat", segments)),
            ChatMessageType.PrivateMessage => (
                $"pm:{packet.SourcePlayer}:{packet.Content}",
                CreatePrivateChat(sender!, segments)),
            _ => (null, null),
        };
    }

    private static ChatText CreateFoldedTypedChat(Color prefixColor, string prefixDialogId, ImmutableArray<ChatTextSegment> content)
        => new ChatText([
            new(prefixColor, Dialog.Clean(prefixDialogId)),
            new(ColorChatContent, " "),
            ..content
        ]);

    private ChatText CreatePublicChat(OnlinePlayer sender, ImmutableArray<ChatTextSegment> content)
        => new ChatText([
            new(sender.Info.Color, sender.GetDisplayName(true, context.ShowAvatar)),
            new(ColorChatContent, ": "),
            ..content
        ]);

    private ChatText CreateMapChat(OnlinePlayer sender, ImmutableArray<ChatTextSegment> content)
        => new ChatText([
            new(ColorMapChat, Dialog.Clean("miaonet_chat_map_chat")),
            new(ColorChatContent, " "),
            new(sender.Info.Color, sender.GetDisplayName(true, context.ShowAvatar)),
            new(ColorChatContent, ": "),
            ..content
        ]);

    private ChatText CreateChannelChat(OnlinePlayer sender, ImmutableArray<ChatTextSegment> content)
        => new ChatText([
            new(ColorChannelChat, Dialog.Clean("miaonet_chat_channel_chat")),
            new(ColorChatContent, " "),
            new(sender.Info.Color, sender.GetDisplayName(true, context.ShowAvatar)),
            new(ColorChatContent, ": "),
            ..content
        ]);

    private ChatText CreatePrivateChat(OnlinePlayer sender, ImmutableArray<ChatTextSegment> content)
        => new ChatText([
            new(
                ColorPrivateChatReceived,
                PFormat.Format(
                    CultureInfo.CurrentCulture,
                    Dialog.Clean("miaonet_chat_whisper_received"),
                    sender.GetDisplayName(false, context.ShowAvatar)
                )
            ),
            new(ColorPrivateChat, ": "),
            ..content
        ]);

    private ChatText CreateSentPrivateChat(OnlinePlayer other, OnlinePlayer self, ImmutableArray<ChatTextSegment> content)
        => new ChatText([
            new(
                ColorPrivateChatReceived,
                PFormat.Format(
                    CultureInfo.CurrentCulture,
                    Dialog.Clean("miaonet_chat_whisper_sent"),
                    other.GetDisplayName(false, context.ShowAvatar),
                    self.GetDisplayName(false, context.ShowAvatar)
                )
            ),
            new(ColorPrivateChat, ": "),
            ..content
        ]);

    private static Color ContentColor(ChatMessageType type)
        => type == ChatMessageType.PrivateMessage ? ColorPrivateChat : ColorChatContent;

    // selfName can be null if we don't care about MentionsSelf(for sent private messages)
    private (ImmutableArray<ChatTextSegment> Segments, bool MentionsSelf) ParseContent(string text, Color defaultColor, string? selfName)
    {
        var players = context.ClientState?.AllPlayers;
        if (string.IsNullOrEmpty(text) || players is null)
            return (ChatText.Parse(text, defaultColor), false);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (OnlinePlayer p in players)
        {
            if (!string.IsNullOrEmpty(p.Info.Name))
                names.Add(p.Info.Name);
        }
        if (names.Count == 0)
            return (ChatText.Parse(text, defaultColor), false);

        bool mentionsSelf = false;
        var builder = ImmutableArray.CreateBuilder<ChatTextSegment>();
        foreach (ChatTextSegment segment in ChatText.Parse(text, defaultColor))
            mentionsSelf |= SplitMentionSegments(builder, segment, names, selfName);
        return (builder.DrainToImmutable(), mentionsSelf);
    }

    private static bool SplitMentionSegments(
        ImmutableArray<ChatTextSegment>.Builder builder,
        ChatTextSegment segment,
        IEnumerable<string> names,
        string? selfName
    )
    {
        bool mentionsSelf = false;
        string text = segment.Text;
        if (text.IndexOf('@', StringComparison.Ordinal) < 0)
        {
            builder.Add(segment);
            return false;
        }

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '@' || (i != 0 && !char.IsWhiteSpace(text[i - 1])))
                continue;

            string? matchedName = MatchName(text, i + 1, names);
            if (matchedName is null)
                continue;

            if (string.Equals(matchedName, selfName, MentionComparison))
                mentionsSelf = true;

            int mentionEnd = i + 1 + matchedName.Length;
            if (i > start)
                builder.Add(new ChatTextSegment(segment.Style, segment.Color, text[start..i]));
            builder.Add(new ChatTextSegment(segment.Style, ColorMention, text[i..mentionEnd]));
            i = mentionEnd - 1;
            start = mentionEnd;
        }

        if (start < text.Length)
            builder.Add(new ChatTextSegment(segment.Style, segment.Color, text[start..]));

        return mentionsSelf;
    }

    private static string? MatchName(string text, int index, IEnumerable<string> names)
    {
        string? best = null;
        foreach (string name in names)
        {
            // find the longest
            if (best is not null && name.Length <= best.Length)
                continue;

            if (index + name.Length > text.Length)
                continue;

            if (!text.AsSpan().Slice(index, name.Length).Equals(name.AsSpan(), MentionComparison))
                continue;

            int end = index + name.Length;
            if (end < text.Length && char.IsLetterOrDigit(text[end]))
                continue;
            best = name;
        }
        return best;
    }
}

public readonly struct ReceivedChatMessage
{
    public ChatText? Text { get; }

    public bool MentionsSelf { get; }

    public ReceivedChatMessage(ChatText? text, bool mentionsSelf)
    {
        Text = text;
        MentionsSelf = mentionsSelf;
    }
}
