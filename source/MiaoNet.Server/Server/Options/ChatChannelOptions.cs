using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class ChatChannelStrings
{
    public string? Name { get; set; }

    public string? Prefix { get; set; }
}

public sealed class ChatChannelOptions
{
    public Dictionary<string, ChatChannelStrings> SChinese { get; set; } = new();

    public Dictionary<string, ChatChannelStrings> English { get; set; } = new();

    public IReadOnlyDictionary<string, ChatChannelStrings> this[LanguageCode language]
        => language == LanguageCode.SChinese ? SChinese : English;
}
