using Celeste.Mod.UI;
using MiaoNet.Shared;

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

        /*
        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_player_opacity"), 1, 10, settings.PlayerOpacity
        ).Change(v => settings.PlayerOpacity = v);
        menu.Add(item);

        item = new TextMenuExt.IntSlider(
            Dialog.Get("miaonet_options_player_name_opacity"), 1, 10, settings.NameOpacity
        ).Change(v => settings.NameOpacity = v);
        menu.Add(item);
        */

        item = new TextMenu.SubHeader(Dialog.Get("miaonet_options_chat"));
        menu.Add(item);

        /*
        item = new TextMenu.Option<string>(Dialog.Get("miaonet_options_new_messages_display"))
            .Add(Dialog.Get("miaonet_options_new_messages_display_chat_only"), null!, false)
            .Add(Dialog.Get("miaonet_options_new_messages_display_system_only"), null!, false)
            .Add(Dialog.Get("miaonet_options_new_messages_display_all"), null!, true);
        menu.Add(item);
        */
    }
}