using System.Diagnostics;
using System.Net;
using Celeste.Mod.UI;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

public static class MenuMiaoNetOptions
{
    public static void BuildHeader(TextMenu menu)
    {
        TextMenu.Item item;
        item = new TextMenu.Header(Dialog.Get("miaonet_options_title"));
        menu.Add(item);
    }

    public static void BuildMenu(TextMenu menu, bool inGame)
    {
        MiaoNetModuleSettings settings = MiaoNetModule.Settings;

        TextMenu.Item item;

        item = new TextMenu.SubHeader($"MiaoNet | v.{MiaoNetModule.Instance.Metadata.VersionString}");
        menu.Add(item);

        // -- MiaoNet --

        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_connected"),
            MiaoNetModule.Instance.MiaoNetContext.HasConnection
        ).Change(v =>
        {
            var context = MiaoNetModule.Instance.MiaoNetContext;
            if (v)
                context.Connect();
            else
                context.Disconnect();
        });
        menu.Add(item);

        #region Login State

        item = new TextMenu.SubHeader(Dialog.Get("miaonet_options_login_state"), false);
        menu.Add(item);

#if USE_CELEMIAO_AUTH
        item = new TextMenu.Button(Dialog.Get("miaonet_options_login"))
        {
            OnPressed = () =>
            {
                ClientRC.Start();

                string url = "https://bbs.celemiao.com/oauth/authorize?" +
                    "client_id=bN8BOz8IjLk981LFLckBq3XzA6fsDC0d" +
                    "&response_type=code" +
                    "&redirect_uri=http://localhost:21472/auth" +
                    "&scope=celeste.read";
                SDL2.SDL.SDL_OpenURL(url);
            }
        };
        menu.Add(item);
        item.AddDescription(menu, Dialog.Clean("miaonet_options_login_tip"));

        if (settings.LastName is not null)
        {
            string loggedInText = Dialog.Get("miaonet_options_last_logged_in_name") + settings.LastName;
            item = new TextMenu.Button(loggedInText);
            menu.Add(item);
        }
#else
        AddAuthPropButton(menu, inGame, Dialog.Get("miaonet_options_custom_auth_name"),
            () => settings.Name, v => settings.Name = v
        );
        AddAuthPropButton(menu, inGame, Dialog.Get("miaonet_options_custom_auth_prefix"),
            () => settings.Prefix, v => settings.Prefix = v
        );
        AddAuthPropButton(menu, inGame, Dialog.Get("miaonet_options_custom_auth_color"),
            () => settings.Color, v => settings.Color = v
        );
        AddAuthPropButton(menu, inGame, Dialog.Get("miaonet_options_custom_auth_avatar_url"),
            () => settings.AvatarUrl, v => settings.AvatarUrl = v
        );

        static void AddAuthPropButton(TextMenu menu, bool inGame, string label, Func<string?> getter, Action<string> setter)
        {
            var button = new TextMenu.Button($"{label} {WithCutOff(getter() ?? string.Empty)}");
            if (!inGame)
            {
                button.OnPressed = () =>
                {
                    Audio.Play(SFX.ui_main_savefile_rename_start);
                    menu.SceneAs<Overworld>()
                        .Goto<OuiModOptionString>()
                        .Init<OuiModOptions>(getter(), v =>
                            {
                                v = v.Trim();
                                setter(v);
                                button.Label = $"{label} {WithCutOff(v)}";
                            }, 36, 2
                        );
                };
            }
            menu.Add(button);
            if (inGame)
                button.AddDescription(menu, Dialog.Get("miaonet_options_custom_auth_tip_in_game"));
            else
                button.AddDescription(menu, Dialog.Get("miaonet_options_custom_auth_tip"));

            static string WithCutOff(string value)
            {
                const int CutOffLength = 24;
                if (value.Length > CutOffLength)
                    value = $"{value.AsSpan()[..CutOffLength]}...";
                return value;
            }
        }
#endif

        #endregion

        #region Connection

        item = new TextMenu.SubHeader(Dialog.Get("miaonet_options_connection"), false);
        menu.Add(item);

        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_connect_on_game_start"),
            settings.ConnectOnGameStart
        ).Change(v => settings.ConnectOnGameStart = v);
        menu.Add(item);

        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_ignore_cert_revocation_status"),
            settings.IgnoreCertRevocationStatus
        ).Change(v => settings.IgnoreCertRevocationStatus = v);
        menu.Add(item);
        item.AddDescription(menu, Dialog.Clean("miaonet_options_ignore_cert_revocation_status_tip"));

        /*
        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_use_sync_refresh"), false
        );
        menu.Add(item);
        item.AddDescription(menu, Dialog.Clean("miaonet_options_use_sync_refresh_tip"));
        */

        #endregion

        #region Visuals

        item = new TextMenu.SubHeader(Dialog.Get("miaonet_options_visuals"), false);
        menu.Add(item);

        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_show_own_name"), settings.ShowOwnName
        ).Change(v => settings.ShowOwnName = v);
        menu.Add(item);

        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_player_light"), settings.PlayerLight
        ).Change(v => settings.PlayerLight = v);
        menu.Add(item);

        #region UI

        var uiSubMenu = new TextMenuExt.SubMenu(Dialog.Get("miaonet_options_ui"), false);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_player_list_ui_scale"), 1, 6, settings.PlayerListUIScale
        ).Change(v => settings.PlayerListUIScale = v);
        uiSubMenu.Add(item);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_chat_ui_scale"), 1, 6, settings.ChatUIScale
        ).Change(v => settings.ChatUIScale = v);
        uiSubMenu.Add(item);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_chat_background_opacity"), 0, 10, settings.ChatBackgroundOpacity
        ).Change(v => settings.ChatBackgroundOpacity = v);
        uiSubMenu.Add(item);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_chat_display_duration"), 1, 12, settings.ChatDisplayDuration
        ).Change(v => settings.ChatDisplayDuration = v);
        uiSubMenu.Add(item);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_idle_chat_height"), 1, 10, settings.IdleChatHeight
        ).Change(v => settings.IdleChatHeight = v);
        uiSubMenu.Add(item);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_active_chat_height"), 1, 10, settings.ActiveChatHeight
        ).Change(v => settings.ActiveChatHeight = v);
        uiSubMenu.Add(item);

        menu.Add(uiSubMenu);

        #endregion

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_player_opacity"), 1, 10, settings.PlayerOpacity
        ).Change(v => settings.PlayerOpacity = v);
        menu.Add(item);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_self_name_opactiy"), 1, 10, settings.SelfNameOpacity
        ).Change(v => settings.SelfNameOpacity = v);
        menu.Add(item);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_player_name_opacity"), 1, 10, settings.PlayerNameOpacity
        ).Change(v => settings.PlayerNameOpacity = v);
        menu.Add(item);

        {
            TextMenuExt.IntSlider minPlayerOpacitySlider;
            minPlayerOpacitySlider = new TextMenuExt.IntSlider(
                Dialog.Get("miaonet_options_min_player_opacity"), 1, 10, settings.MinPlayerOpacity
            ).Change(v => settings.MinPlayerOpacity = v);
            minPlayerOpacitySlider.Visible = settings.DistanceBasedOpacity;

            item = new TextMenu.OnOff(
                Dialog.Get("miaonet_options_distance_based_opacity"), settings.DistanceBasedOpacity
            ).Change(v => settings.DistanceBasedOpacity = minPlayerOpacitySlider.Visible = v);
            menu.Add(item);
            menu.Add(minPlayerOpacitySlider);
        }

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_emote_opacity"), 1, 10, settings.EmoteOpacity
        ).Change(v => settings.EmoteOpacity = v);
        menu.Add(item);

        item = new EnumSlider<JumpthruType>(
            Dialog.Get("miaonet_options_group_photo_platform_type"),
            t => t.ToString(), settings.GroupPhotoPlatformType
        ).Change(v => settings.GroupPhotoPlatformType = v);
        menu.Add(item);

        #endregion

        #region Audio

        item = new TextMenu.SubHeader(Dialog.Get("miaonet_options_audio"), false);
        menu.Add(item);

        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_player_audio"), settings.PlayerAudio
        ).Change(v => settings.PlayerAudio = v);
        menu.Add(item);
        item.AddDescription(menu, Dialog.Clean("miaonet_options_player_audio_tip"));

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_player_audio_volume"), 1, 10, settings.PlayerAudioVolume
        ).Change(v => settings.PlayerAudioVolume = v);
        menu.Add(item);

        #endregion

        #region Interactions

        item = new TextMenu.SubHeader(Dialog.Get("miaonet_options_interactions"), false);
        menu.Add(item);

        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_player_interactions"), settings.PlayerInteractions
        ).Change(v => settings.PlayerInteractions = v);
        menu.Add(item);

        // if not using celemiao auth then this is meaningless
#if USE_CELEMIAO_AUTH
        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_live_mode"), settings.LiveMode
        ).Change(v => settings.LiveMode = v);
        menu.Add(item);
        item.AddDescription(menu, Dialog.Clean("miaonet_options_live_mode_tip"));
#endif

        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_fireworks"), settings.Fireworks
        ).Change(v => settings.Fireworks = v);
        menu.Add(item);
        item.AddDescription(menu, Dialog.Clean("miaonet_options_fireworks_tip"));

        item = new TextMenu.Button(
            Dialog.Get("miaonet_options_open_settings_file")
        ).Pressed(() =>
        {
            string path = Path.Combine(Everest.PathSettings, "modsettings-MiaoNet.celeste");
            if (!File.Exists(path))
                MiaoNetModule.Instance.SaveSettings();
            ProcessStartInfo psi = new()
            {
                FileName = path,
                UseShellExecute = true
            };
            Process.Start(psi);
        });
        menu.Add(item);
        item.AddDescription(menu, Dialog.Clean("miaonet_options_open_settings_file_tip"));

        item = new TextMenu.Button(
            Dialog.Get("miaonet_options_reload_emote_settings")
        ).Pressed(() =>
        {
            // load settings will not call on input initialize
            // so let's do this like CelesteNet...
            var o = MiaoNetModule.Settings;
            MiaoNetModule.Instance.LoadSettings();
            var n = MiaoNetModule.Settings;
            o.Emotes = n.Emotes;
            MiaoNetModule.Instance._Settings = o;
        });
        menu.Add(item);

        #endregion

        #region Behaviours

        item = new TextMenu.SubHeader(Dialog.Get("miaonet_options_behaviours"), false);
        menu.Add(item);

        item = new EnumSlider<ButtonMode>(
            Dialog.Get("miaonet_options_player_list_button_mode"),
            e => Dialog.Get($"miaonet_options_player_list_button_mode_{e}"),
            settings.PlayerListButtonMode
        ).Change(v => settings.PlayerListButtonMode = v);
        menu.Add(item);

        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_teleport_temp_save"), settings.TeleportTempSave
        ).Change(v => settings.TeleportTempSave = v);
        menu.Add(item);
        item.AddDescription(menu, Dialog.Clean("miaonet_options_teleport_temp_save_tip"));

        item = new EnumSlider<TeleportBehaviour>(
            Dialog.Get("miaonet_options_teleport_behaviour"),
            e => Dialog.Get($"miaonet_options_teleport_behaviour_{e}"),
            settings.TeleportBehaviour
        ).Change(v => settings.TeleportBehaviour = v);
        menu.Add(item);

        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_player_presence_message"),
            settings.PlayerPresenceMessages
        ).Change(v => settings.PlayerPresenceMessages = v);
        menu.Add(item);

        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_no_new_messages_showing"),
            settings.NoNewMessagesShowing
        ).Change(v => settings.NoNewMessagesShowing = v);
        menu.Add(item);
        item.AddDescription(menu, Dialog.Clean("miaonet_options_no_new_messages_showing_tip"));

        #endregion

        AddKeyBindingsSection(menu, inGame);
    }

    public static void AddKeyBindingsSection(TextMenu menu, bool _)
    {
        menu.Add(new TextMenu.SubHeader(Dialog.Get("miaonet_options_key_bindings"), false));
        // partially copied from everest 
        menu.Add(new TextMenu.Button(Dialog.Clean("options_keyconfig")).Pressed(delegate
        {
            menu.Focused = false;
            Engine.Scene.Add(new MiaoNetKeyboardConfigUI(MiaoNetModule.Settings)
            {
                OnClose = () =>
                {
                    menu.Focused = true;
                    MiaoNetModule.Instance.OnInputInitialize();
                }
            });
            Engine.Scene.OnEndOfFrame += delegate
            {
                Engine.Scene.Entities.UpdateLists();
            };
        }));
        menu.Add(new TextMenu.Button(Dialog.Clean("options_btnconfig")).Pressed(delegate
        {
            menu.Focused = false;
            Engine.Scene.Add(new MiaoNetButtonConfigUI(MiaoNetModule.Settings)
            {
                OnClose = () =>
                {
                    menu.Focused = true;
                    MiaoNetModule.Instance.OnInputInitialize();
                }
            });
            Engine.Scene.OnEndOfFrame += delegate
            {
                Engine.Scene.Entities.UpdateLists();
            };
        }));
    }

    private class EnumSlider<T> : TextMenu.Option<T> where T : struct, Enum
    {
        public EnumSlider(string label, Func<T, string> enumLabelSelector, T startValue = default)
            : base(label)
        {
            foreach (T enumValue in Enum.GetValues(typeof(T)))
                Add(enumLabelSelector(enumValue), enumValue, enumValue.Equals(startValue));
        }
    }

    private class MiaoNetKeyboardConfigUI : KeyboardConfigUI
    {
        private readonly MiaoNetModuleSettings settings;

        public MiaoNetKeyboardConfigUI(MiaoNetModuleSettings settings)
        {
            this.settings = settings;

            // copied from everest ModuleSettingsKeyboardConfigUI
            if (Engine.Scene is Level level)
            {
                bool? oldAllowHudHide = null;
                OnUpdate = () =>
                {
                    if (oldAllowHudHide == null)
                    {
                        oldAllowHudHide = level.AllowHudHide;
                        level.AllowHudHide = false;
                        OnClose += () => level.AllowHudHide = oldAllowHudHide.Value;
                    }
                };
            }
            Reload(2);
        }

        public override void Reload(int index = -1)
        {
            // Reload will be called in parent's ctor
            if (settings is null)
                return;

            Clear();
            Add(new Header(Dialog.Clean("KEY_CONFIG_TITLE")));
            Add(new InputMappingInfo(false));

            AddMapForceLabel(Dialog.Get("miaonet_options_button_chat"), settings.ChatButton.Binding);
            AddMapForceLabel(Dialog.Get("miaonet_options_button_chat_command"), settings.ChatCommandButton.Binding);
            AddMapForceLabel(Dialog.Get("miaonet_options_button_player_list"), settings.PlayerListButton.Binding);
            AddMapForceLabel(Dialog.Get("miaonet_options_button_create_fireworks"), settings.CreateFireworksButton.Binding);
            AddMapForceLabel(Dialog.Get("miaonet_options_button_player_list_scroll_up"), settings.PlayerListScrollUp.Binding);
            AddMapForceLabel(Dialog.Get("miaonet_options_button_player_list_scroll_down"), settings.PlayerListScrollDown.Binding);

            while (settings.EmoteButtons.Count < settings.Emotes.Count)
                settings.EmoteButtons.Add(new());

            Add(new SubHeader(Dialog.Get("miaonet_options_button_emotes")));
            for (int i = 0; i < settings.Emotes.Count; i++)
                AddMapForceLabel(
                    Dialog.Get("miaonet_options_button_emote_i").Replace("(0)", (i + 1).ToString()),
                    settings.EmoteButtons[i].Binding
                );

            Add(new SubHeader(string.Empty));
            Add(new Button(Dialog.Clean("KEY_CONFIG_RESET"))
            {
                IncludeWidthInMeasurement = false,
                AlwaysCenter = true,
                OnPressed = ResetPressed
            });

            if (index >= 0)
                Selection = index;
        }

        public override void Reset()
        {
            settings.ResetKeyBindings();

            Input.Initialize();
            Reload(Selection);
        }
    }

    private class MiaoNetButtonConfigUI : ButtonConfigUI
    {
        private readonly MiaoNetModuleSettings settings;

        public MiaoNetButtonConfigUI(MiaoNetModuleSettings settings)
        {
            this.settings = settings;
            // copied from everest ModuleSettingsKeyboardConfigUI
            All.Add(Buttons.Back);
            All.Add(Buttons.BigButton);
            All.Add(Buttons.RightStick);
            All.Add(Buttons.LeftStick);
            if (Engine.Scene is Level level)
            {
                bool? oldAllowHudHide = null;
                OnUpdate = () =>
                {
                    if (oldAllowHudHide == null)
                    {
                        oldAllowHudHide = level.AllowHudHide;
                        level.AllowHudHide = false;
                        OnClose += () => level.AllowHudHide = oldAllowHudHide.Value;
                    }
                };
            }
            Reload(2);
        }

        public override void Reload(int index = -1)
        {
            // Reload will be called in parent's ctor
            if (settings is null)
                return;

            Clear();
            Add(new Header(Dialog.Clean("BTN_CONFIG_TITLE")));
            Add(new InputMappingInfo(false));

            AddMapForceLabel(Dialog.Get("miaonet_options_button_chat"), settings.ChatButton.Binding);
            AddMapForceLabel(Dialog.Get("miaonet_options_button_chat_command"), settings.ChatCommandButton.Binding);
            AddMapForceLabel(Dialog.Get("miaonet_options_button_player_list"), settings.PlayerListButton.Binding);
            AddMapForceLabel(Dialog.Get("miaonet_options_button_create_fireworks"), settings.CreateFireworksButton.Binding);
            AddMapForceLabel(Dialog.Get("miaonet_options_button_player_list_scroll_up"), settings.PlayerListScrollUp.Binding);
            AddMapForceLabel(Dialog.Get("miaonet_options_button_player_list_scroll_down"), settings.PlayerListScrollDown.Binding);
            AddMapForceLabel(Dialog.Get("miaonet_options_button_emote_wheel_send_emote"), settings.EmoteWheelSendEmote.Binding);

            Add(new SubHeader(string.Empty));
            Add(new Button(Dialog.Clean("KEY_CONFIG_RESET"))
            {
                IncludeWidthInMeasurement = false,
                AlwaysCenter = true,
                OnPressed = ResetPressed
            });

            if (index >= 0)
                Selection = index;
        }

        public override void Reset()
        {
            settings.ResetKeyBindings();

            Input.Initialize();
            Reload(Selection);
        }
    }
}