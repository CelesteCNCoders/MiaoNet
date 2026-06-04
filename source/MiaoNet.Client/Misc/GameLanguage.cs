using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public static class GameLanguage
{
    public static string GetRCLang(string languageID) => languageID switch
    {
        "schinese" => "zhs",
        "english" => "en",
        _ => "en"
    };

    public static LanguageCode GetLanguageCode(string languageID) => languageID switch
    {
        "schinese" => LanguageCode.SChinese,
        "english" => LanguageCode.English,
        _ => LanguageCode.English
    };
}
