using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class LocalizedOptions<TOptions>
{
    public required TOptions SChinese { get; set; }

    public required TOptions English { get; set; }

    public TOptions Get(LanguageCode languageCode) => languageCode switch
    {
        LanguageCode.SChinese => SChinese,
        LanguageCode.English => English,
        _ => English
    };

    public TOptions this[LanguageCode languageCode] => Get(languageCode);
}
