namespace Celeste.Mod.MiaoNet;

public static class GameLanguage
{
    public static string GetRCLang() => Dialog.Language.Id switch
    {
        "schinese" => "zhs",
        "english" => "en",
        _ => "en"
    };

    public static byte GetLangCode() => Dialog.Language.Id switch
    {
        "schinese" => 0,
        "english" => 1,
        _ => 1
    };
}
