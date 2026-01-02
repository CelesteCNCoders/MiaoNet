using Microsoft.Xna.Framework.Input;
using YamlDotNet.Serialization;

namespace Celeste.Mod.MiaoNet;

#pragma warning disable CS8618

public enum ButtonMode
{
    Press,
    Hold
}

public enum TeleportBehaviour
{
    NoSession,
    WithSession
}

// note: menus for this settings are all created and handled manually
// so all everest attributes will have no effect
// check MenuMiaoNetOptions for more details
public sealed class MiaoNetModuleSettings : EverestModuleSettings
{
    #region Login State

    public string Name { get; set; }

    #endregion

    #region Connection

    public bool ConnectOnGameStart { get; set; }

    #endregion

    #region Visuals

    public bool ShowOwnName { get; set; } = true;

    public int UIScale { get; set; } = 4;

    [YamlIgnore]
    public float UIScaleValue => UIScale switch
    {
        1 => 3f / 12f,
        2 => 5f / 12f,
        3 => 7f / 12f,
        4 => 8f / 12f,
        5 => 10f / 12f,
        6 => 12f / 12f
    };

    public int PlayerOpacity { get; set; } = 8;

    [YamlIgnore] public float PlayerOpacityValue => PlayerOpacity / 10f;

    public int SelfNameOpacity { get; set; } = 8;

    [YamlIgnore] public float SelfNameOpacityValue => SelfNameOpacity / 10f;

    public int NameOpacity { get; set; } = 8;

    [YamlIgnore] public float NameOpacityValue => NameOpacity / 10f;

    #endregion

    #region Interactions

    public ButtonMode PlayerListButtonMode { get; set; }

    public TeleportBehaviour TeleportBehaviour { get; set; }

    #endregion

    #region Button Bindings

    public ButtonBinding ChatButton { get; set; }

    public ButtonBinding PlayerListButton { get; set; }

    #endregion

    #region

    public int EmotesCount { get; set; } = 8;

    public List<ButtonBinding> EmoteButtons { get; set; }

    public List<string> Emotes { get; set; }

    #endregion

    public MiaoNetModuleSettings()
    {
        ResetKeyBindings();
        ResetEmotes();
    }

    public void ResetKeyBindings()
    {
        ChatButton = new(0, Keys.T);
        PlayerListButton = new(0, Keys.Tab);
        List<ButtonBinding> bindings = new();
        for (int i = 0; i < EmotesCount; i++)
            bindings.Add(new(0, i < 8 ? Keys.D1 + i : Keys.None));
        EmoteButtons = bindings;
    }

    public void ResetEmotes()
    {
        EmotesCount = 8;
        Emotes = [
            "i:collectables/heartgem/0/spin",
            "i:collectables/strawberry",
            "Hi!",
            "Too slow!",
            "p:madeline/normal04",
            "p:ghost/scoff03",
            "p:theo/yolo0 3 2 1 2 !",
            "p:granny/laugh"
        ];
    }
}