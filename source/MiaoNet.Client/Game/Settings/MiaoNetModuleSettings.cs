using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework.Input;
using YamlDotNet.Serialization;

namespace Celeste.Mod.MiaoNet;

// note: menus for this settings are all created and handled manually
// so all everest attributes will have no effect
// check MenuMiaoNetOptions for more details
public sealed class MiaoNetModuleSettings : EverestModuleSettings,
    INotifySettingsChanged<MiaoNetModuleSettings>
{
    public event SettingsChangedEventHandler<MiaoNetModuleSettings>? SettingsChanged;

    #region Login State

#if USE_CELEMIAO_AUTH

    // encrypted using the user's environment string so that 
    // someone can't just leak it by taking a screenshot of the settings file.
    [YamlIgnore]
    public byte[]? TokenData { get; set; }

    // This is for Serializer
    // but we can't make it private...
    public string? TokenDataEncrypted
    {
        get => TokenData is null ? null : TokenDataUtils.Encrypt(TokenData);
        set
        {
            if (value is null)
            {
                TokenData = null;
                return;
            }
            TokenData = TokenDataUtils.TryDecrypt(value, out byte[]? tokenData)
                ? tokenData
                : null;
        }
    }

    public string? LastName { get; set; }

#else

    public string? Name { get; set; }

    public string? Prefix { get; set; }

    public string? Color { get; set; }

    public string? AvatarUrl { get; set; }

#endif

    #endregion

    #region Connection

    public bool ConnectOnGameStart { get; set; }

    // This should be a temporary option
    [YamlIgnore]
    public bool IgnoreCertRevocationStatus { get; set; }

    #endregion

    #region Visuals

    public bool ShowAvatar { get; set; } = true;

    public bool ShowOwnName { get; set; } = true;

    public bool PlayerLight { get; set; } = false;

    #region UI

    public int PlayerListUIScale 
    {
        get => field; 
        set { field = value; NotifySettingsChanged(SettingsCategory.VisualsUI); } 
    } = 4;

    public int ChatUIScale 
    {
        get => field; 
        set { field = value; NotifySettingsChanged(SettingsCategory.VisualsUI); }
    } = 4;

    public int ChatBackgroundOpacity
    { 
        get => field; 
        set { field = value; NotifySettingsChanged(SettingsCategory.VisualsUI); } 
    } = 8;

    public int ChatTextOpacity
    {
        get => field; 
        set { field = value; NotifySettingsChanged(SettingsCategory.VisualsUI); }
    } = 10;

    public int ChatDisplayDuration 
    {
        get => field; 
        set { field = value; NotifySettingsChanged(SettingsCategory.VisualsUI); }
    } = 8;

    public int IdleChatHeight
    {
        get => field; 
        set { field = value; NotifySettingsChanged(SettingsCategory.VisualsUI); }
    } = 4;

    public int ActiveChatHeight 
    { 
        get => field; 
        set { field = value; NotifySettingsChanged(SettingsCategory.VisualsUI); }
    } = 8;

    #endregion

    public int PlayerOpacity { get; set; } = 8;

    public int PlayerNameOpacity { get; set; } = 8;

    public int SelfNameOpacity { get; set; } = 8;

    public bool DistanceBasedOpacity { get; set; } = false;

    public int MinPlayerOpacityMultiplier { get; set; } = 2;

    public int EmoteOpacity { get; set; } = 10;

    public JumpthruType GroupPhotoPlatformType { get; set; } = JumpthruType.Dream;

    public SyncMode FollowersSyncMode { get; set; } = SyncMode.Both;

    public ClipType PlayerListMapNameClipType
    { 
        get => field; 
        set { field = value; NotifySettingsChanged(SettingsCategory.PlayerList); }
    } = ClipType.None;

    #region Calculated

    [YamlIgnore] public float PlayerListUIScaleValue => GetScaleValue(PlayerListUIScale);

    [YamlIgnore] public float ChatUIScaleValue => GetScaleValue(ChatUIScale);

    [YamlIgnore] public float PlayerOpacityValue => PlayerOpacity / 10f;

    [YamlIgnore] public float PlayerNameOpacityValue => PlayerNameOpacity / 10f;

    [YamlIgnore] public float SelfNameOpacityValue => SelfNameOpacity / 10f;

    [YamlIgnore] public float MinPlayerOpacityValue => MinPlayerOpacityMultiplier / 10f;

    [YamlIgnore] public float EmoteOpacityValue => EmoteOpacity / 10f;

    [YamlIgnore] public float ChatBackgroundOpacityValue => ChatBackgroundOpacity / 10f;

    [YamlIgnore] public float ChatTextOpacityValue => ChatTextOpacity / 10f;

    #endregion

    #endregion

    #region Audio

    public SyncMode PlayerAudioSyncMode { get; set; } = SyncMode.Both;

    public int PlayerAudioVolume { get; set; } = 5;

    [YamlIgnore] public float PlayerAudioVolumeValue => PlayerAudioVolume / 10f;

    #endregion

    #region Interactions

    public bool PlayerInteractions { get; set; } = true;

    [YamlIgnore]
    public bool LiveMode { get; set; }

    [YamlIgnore]
    public bool Fireworks { get; set; } = true;

    // emote 配置允许玩家手改 yaml, 所以反序列化后可能出现 null 之类的
    // 比如玩家可能会直接把 EmoteButtons 写空 (除了 voidsd 之外真的会有人这么做吗)
    // 目前 getter 会把 null 规范化成空列表
    // 但这只保证列表对象非 null, 不保证其中的按键配置和 emote 配置一定完整或合法
    // 使用方仍然需要继续处理空字符串/非法格式之类的
    public List<ButtonBinding> EmoteButtons
    {
        get => field ??= [];
        set => field = value;
    }

    public List<string> Emotes
    {
        get => field ??= [];
        set => field = value;
    }

    #endregion

    #region Behaviours

    public ButtonMode PlayerListButtonMode { get; set; }

    public bool EnableEmoteWheel { get; set; } = true;

    public bool TeleportTempSave { get; set; } = true;

    public TeleportBehaviour TeleportBehaviour { get; set; } = TeleportBehaviour.WithSession;

    public bool PlayerPresenceMessages { get; set; } = true;

    [YamlIgnore] public bool NoNewMessagesShowing { get; set; }

    #endregion

    #region Button Bindings

    public ButtonBinding ChatButton { get; set; }

    public ButtonBinding ChatCommandButton { get; set; }

    public ButtonBinding PlayerListButton { get; set; }

    public ButtonBinding CreateFireworksButton { get; set; }

    public ButtonBinding PlayerListScrollUp { get; set; }

    public ButtonBinding PlayerListScrollDown { get; set; }

    public ButtonBinding EmoteWheelSendEmote { get; set; }

    #endregion

    #region 

    public bool TippedTeleport { get; set; }

    [YamlIgnore] public bool GroupPhotoMode { get; set; }

    #endregion

    public MiaoNetModuleSettings()
    {
        ResetEmotes();
        ResetKeyBindings();
    }

    [MemberNotNull(
        nameof(ChatButton),
        nameof(ChatCommandButton),
        nameof(PlayerListButton),
        nameof(EmoteButtons),
        nameof(CreateFireworksButton),
        nameof(PlayerListScrollUp),
        nameof(PlayerListScrollDown),
        nameof(EmoteWheelSendEmote)
    )]
    public void ResetKeyBindings()
    {
        ChatButton = new(0, Keys.T);
        ChatCommandButton = new(0, 0);
        PlayerListButton = new(0, Keys.Tab);
        List<ButtonBinding> bindings = new();
        for (int i = 0; i < Emotes.Count; i++)
            bindings.Add(new(0, i < 8 ? Keys.D1 + i : Keys.None));
        EmoteButtons = bindings;
        CreateFireworksButton = new(0, 0);
        PlayerListScrollUp = new(0, Keys.PageUp);
        PlayerListScrollDown = new(0, Keys.PageDown);
        EmoteWheelSendEmote = new(Buttons.RightStick, 0);
    }

    [MemberNotNull(nameof(Emotes))]
    public void ResetEmotes()
    {
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
    {
        return [
            ChatButton, ChatCommandButton, PlayerListButton,
            .. EmoteButtons,
            CreateFireworksButton,
            PlayerListScrollUp, PlayerListScrollDown,
            EmoteWheelSendEmote
        ];
    }

    private static float GetScaleValue(int scale) => scale switch
    {
        1 => 4f,
        2 => 6f,
        3 => 8f,
        4 => 10f,
        5 => 12f,
        6 => 20f,
    } / 24f;

    private void NotifySettingsChanged(SettingsCategory category)
        => SettingsChanged?.Invoke(this, category);
}