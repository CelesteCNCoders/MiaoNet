using Microsoft.Xna.Framework.Input;
using YamlDotNet.Serialization;

namespace Celeste.Mod.MiaoNet;

#pragma warning disable CS8618

public sealed class MiaoNetModuleSettings : EverestModuleSettings
{
    public string Name { get; set; }

    public bool ConnectOnGameStart { get; set; }

    public int UIScale { get; set; } = 4;

    [YamlIgnore, SettingIgnore]
    public float UIScaleValue => UIScale switch
    {
        1 => 3f / 12f,
        2 => 5f / 12f,
        3 => 7f / 12f,
        4 => 8f / 12f,
        5 => 10f / 12f,
        6 => 12f / 12f
    };

    public bool ShowOwnName { get; set; } = true;

    public int PlayerOpacity { get; set; } = 8;

    public int NameOpacity { get; set; } = 8;

    [DefaultButtonBinding(0, Keys.T)]
    public ButtonBinding ChatButton { get; set; }
}