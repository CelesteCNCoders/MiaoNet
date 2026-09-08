using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class AnnouncementsOptions
{
    public required AnnouncementsStrings SChinese { get; set; }

    public required AnnouncementsStrings English { get; set; }

    public static bool IsConfigured(AnnouncementsOptions options)
        => options.SChinese is not null && options.English is not null;

    public AnnouncementsStrings Get(LanguageCode languageCode) => languageCode switch
    {
        LanguageCode.SChinese => SChinese,
        LanguageCode.English => English,
        _ => English
    };

    public AnnouncementsStrings this[LanguageCode languageCode] => Get(languageCode);
}
