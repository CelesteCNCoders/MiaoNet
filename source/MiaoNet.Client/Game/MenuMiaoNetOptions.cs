using System.Diagnostics;
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

        // -- Login State --

        item = new TextMenu.SubHeader(Dialog.Get("miaonet_options_login_state"));
        menu.Add(item);

        string loggedInText = Dialog.Get("miaonet_options_logged_in") + settings.Name;
        if (inGame)
        {
            item = new TextMenu.Button(loggedInText);
            menu.Add(item);
            item.AddDescription(menu, Dialog.Get("miaonet_options_logged_in_tips"));
        }
        else
        {
            TextMenu.Button thisButton =
            thisButton = new TextMenu.Button(loggedInText);
            thisButton.Pressed(() =>
            {
                Audio.Play("event:/ui/main/savefile_rename_start");
                menu.SceneAs<Overworld>()
                    .Goto<OuiModOptionString>()
                    .Init<OuiModOptions>(
                        settings.Name,
                        v =>
                        {
                            settings.Name = v;
                            thisButton.Label = Dialog.Get("miaonet_options_logged_in") + v;
                        }
                    );
            });
            item = thisButton;
            menu.Add(item);
            item.AddDescription(menu, Dialog.Get("miaonet_options_logged_in_tips_2"));
        }

        // -- Connection --

        item = new TextMenu.SubHeader(Dialog.Get("miaonet_options_connection"));
        menu.Add(item);

        /*
        item = new TextMenu.OnOff(Dialog.Get("miaonet_options_auto_reconnect"), false);
        menu.Add(item);
        */

        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_connect_on_game_start"),
            settings.ConnectOnGameStart
        ).Change(v => settings.ConnectOnGameStart = v);
        menu.Add(item);

        // -- Visuals --

        item = new TextMenu.SubHeader(Dialog.Get("miaonet_options_visuals"));
        menu.Add(item);

        item = new TextMenu.OnOff(
            Dialog.Get("miaonet_options_show_own_name"), settings.ShowOwnName
        ).Change(v => settings.ShowOwnName = v);
        menu.Add(item);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_ui_scale"), 1, 6, settings.UIScale
        ).Change(v => settings.UIScale = v);
        menu.Add(item);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_player_opacity"), 1, 10, settings.PlayerOpacity
        ).Change(v => settings.PlayerOpacity = v);
        menu.Add(item);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_self_player_name_opacity"), 1, 10, settings.SelfNameOpacity
        ).Change(v => settings.SelfNameOpacity = v);
        menu.Add(item);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_player_name_opacity"), 1, 10, settings.NameOpacity
        ).Change(v => settings.NameOpacity = v);
        menu.Add(item);

        item = new EnumSlider<MiaoNetModuleSettings.ButtonMode>(
            Dialog.Get("miaonet_options_player_list_button_mode"),
            e => Dialog.Get($"miaonet_options_player_list_button_mode_{e}"),
            settings.PlayerListButtonMode
        ).Change(v => settings.PlayerListButtonMode = v);
        menu.Add(item);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_emotes_count"), 8, 32, settings.EmotesCount
        ).Change(v => settings.EmotesCount = v);
        menu.Add(item);
        item.AddDescription(menu, Dialog.Clean("miaonet_options_emotes_counts_tip"));

        item = new TextMenu.Button(
            Dialog.Get("miaonet_options_open_settings_file")
        ).Pressed(() =>
        {
            ProcessStartInfo psi = new()
            {
                FileName = Path.Combine(Everest.PathSettings, "modsettings-MiaoNet.celeste"),
                UseShellExecute = true
            };
            Process.Start(psi);
        });
        menu.Add(item);
        item.AddDescription(menu, Dialog.Get("miaonet_options_open_settings_file_tip"));

        item = new TextMenu.Button(
            Dialog.Get("miaonet_options_reload_emote_settings")
        ).Pressed(() =>
        {
            // load settings will not call on input initialize
            // so let's behave like CelesteNet...
            var o = MiaoNetModule.Settings;
            MiaoNetModule.Instance.LoadSettings();
            var n = MiaoNetModule.Settings;
            o.Emotes = n.Emotes;
            MiaoNetModule.Instance._Settings = o;
        });
        menu.Add(item);

        // -- Chat --

        /*
        item = new TextMenu.SubHeader(Dialog.Get("miaonet_options_chat"));
        menu.Add(item);

        item = new TextMenu.Option<string>(Dialog.Get("miaonet_options_new_messages_display"))
            .Add(Dialog.Get("miaonet_options_new_messages_display_chat_only"), null!, false)
            .Add(Dialog.Get("miaonet_options_new_messages_display_system_only"), null!, false)
            .Add(Dialog.Get("miaonet_options_new_messages_display_all"), null!, true);
        menu.Add(item);
        */
    }

    public static void AddKeyBindingsSection(TextMenu menu, bool _)
    {
        menu.Add(new TextMenu.SubHeader(Dialog.Get("miaonet_options_key_bindings")));
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

            AddMapForceLabel("Chat Button", settings.ChatButton.Binding);
            AddMapForceLabel("Player List Button", settings.PlayerListButton.Binding);

            while (settings.EmoteButtons.Count < settings.EmotesCount)
                settings.EmoteButtons.Add(new());

            Add(new SubHeader("Emotes"));
            for (int i = 0; i < settings.EmotesCount; i++)
                AddMapForceLabel($"Emote {i + 1}", settings.EmoteButtons[i].Binding);

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

            AddMapForceLabel("Chat Button", settings.ChatButton.Binding);
            AddMapForceLabel("Player List Button", settings.PlayerListButton.Binding);

            while (settings.EmoteButtons.Count < settings.EmotesCount)
                settings.EmoteButtons.Add(new());

            Add(new SubHeader("Emotes"));
            for (int i = 0; i < settings.EmotesCount; i++)
                AddMapForceLabel($"Emote {i + 1}", settings.EmoteButtons[i].Binding);

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