using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

#pragma warning disable CS8618

public sealed class MiaoNetModuleSettings : EverestModuleSettings
{
    public string Name { get; set; }

    public int PlayerOpacity { get; set; } = 8;

    public int NameOpacity { get; set; } = 8;

    [DefaultButtonBinding(0, Keys.T)]
    public ButtonBinding ChatButton { get; set; }
}