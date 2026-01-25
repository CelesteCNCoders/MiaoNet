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

    // This should be a temporary option
    [YamlIgnore]
    public bool IgnoreCertRevocationStatus { get; set; }

    #endregion

    #region Visuals

    public bool ShowOwnName { get; set; } = true;

    public bool PlayerLight { get; set; } = false;

    #region UI

    public int PlayerListUIScale { get; set; } = 4;

    public int ChatUIScale { get; set; } = 4;

    public int ChatBackgroundOpacity { get; set; } = 5;

    public int ChatTextOpacity { get; set; } = 10;

    public int IdleChatHeight { get; set; } = 4;

    public int ActiveChatHeight { get; set; } = 8;

    #endregion

    public int PlayerOpacity { get; set; } = 8;

    public int PlayerNameOpacity { get; set; } = 8;

    public int SelfNameOpacity { get; set; } = 8;

    #region Calculated

    [YamlIgnore] public float PlayerListUIScaleValue => GetScaleValue(PlayerListUIScale);

    [YamlIgnore] public float ChatUIScaleValue => GetScaleValue(ChatUIScale);

    [YamlIgnore] public float PlayerOpacityValue => PlayerOpacity / 10f;

    [YamlIgnore] public float SelfNameOpacityValue => SelfNameOpacity / 10f;

    [YamlIgnore] public float PlayerNameOpacityValue => PlayerNameOpacity / 10f;

    [YamlIgnore] public float ChatBackgroundOpacityValue => ChatBackgroundOpacity / 10f;

    [YamlIgnore] public float ChatTextOpacityValue => ChatTextOpacity / 10f;

    #endregion

    #endregion

    #region Audio

    public bool PlayerAudio { get; set; } = true;

    public int PlayerAudioVolume { get; set; } = 5;

    [YamlIgnore] public float PlayerAudioVolumeValue => PlayerAudioVolume / 10f;

    #endregion

    #region Interactions

    public bool PlayerInteractions { get; set; }

    [YamlIgnore]
    public bool LiveMode { get; set; }

    public ButtonMode PlayerListButtonMode { get; set; }

    public TeleportBehaviour TeleportBehaviour { get; set; }

    public int EmotesCount { get; set; } = 8;

    public List<ButtonBinding> EmoteButtons { get; set; }

    public List<string> Emotes { get; set; }

    #endregion

    #region Button Bindings

    public ButtonBinding ChatButton { get; set; }

    public ButtonBinding PlayerListButton { get; set; }

    public ButtonBinding CreateFireworksButton { get; set; }

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
        CreateFireworksButton = new(0, Keys.F);
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

    public IEnumerable<ButtonBinding> GetButtonBindings()
        => [ChatButton, PlayerListButton, .. EmoteButtons, CreateFireworksButton];

    private static float GetScaleValue(int scale) => scale switch
    {
        1 => 4f,
        2 => 6f,
        3 => 8f,
        4 => 10f,
        5 => 12f,
        6 => 20f,
    } / 24f;
}