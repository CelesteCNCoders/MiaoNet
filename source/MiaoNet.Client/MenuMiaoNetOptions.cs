using Celeste.Mod.UI;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public static class MenuMiaoNetOptions
{
    public static void BuildHeader(TextMenu menu)
    {
        TextMenu.Item item;
        item = new TextMenu.Header("MiaoNet Options");
        menu.Add(item);
    }

    public static void BuildMenu(TextMenu menu, bool inGame)
    {
        TextMenu.Item item;

        item = new TextMenu.SubHeader($"MiaoNet | v.{MiaoNetModule.Instance.Metadata.VersionString}");
        menu.Add(item);

        // -- MiaoNet --

        item = new TextMenu.OnOff("Connected", MiaoNetModule.Instance.MiaoNetContext.HasConnection).Change(v =>
        {
            var context = MiaoNetModule.Instance.MiaoNetContext;
            if (v)
                context.Connect();
            else
                context.Disconnect();
        });
        menu.Add(item);

        // -- Login State --

        item = new TextMenu.SubHeader("Login State");
        menu.Add(item);

        if (inGame)
        {
            item = new TextMenu.Button($"Logged in: {MiaoNetModule.Settings.Name}");
            menu.Add(item);
            item.AddDescription(menu, "Goto mod options in main menu to login or switch logged in account.");
        }
        else
        {
            item = new TextMenu.Button($"Logged in: {MiaoNetModule.Settings.Name}").Pressed(() =>
            {
                Audio.Play("event:/ui/main/savefile_rename_start");
                menu.SceneAs<Overworld>()
                    .Goto<OuiModOptionString>()
                    .Init<OuiModOptions>(
                        MiaoNetModule.Settings.Name,
                        v => MiaoNetModule.Settings.Name = v
                    );
            });
            menu.Add(item);
            item.AddDescription(menu, "Press to login or switch logged in account.");
        }

        // -- Connection --

        item = new TextMenu.SubHeader("Connection");
        menu.Add(item);

        item = new TextMenu.OnOff("Auto Reconnect", false);
        menu.Add(item);

        item = new TextMenu.OnOff("Connect On Game Start", false);
        menu.Add(item);

        // -- Visual --

        item = new TextMenu.SubHeader("Visual");
        menu.Add(item);

        item = new TextMenu.OnOff("Show Own Name", false);
        menu.Add(item);

        item = new TextMenuExt.IntSlider("Player Opacity", 0, 10, MiaoNetModule.Settings.PlayerOpacity)
            .Change(v => MiaoNetModule.Settings.PlayerOpacity = v);
        menu.Add(item);

        item = new TextMenuExt.IntSlider("Name Opacity", 0, 10, MiaoNetModule.Settings.NameOpacity)
            .Change(v => MiaoNetModule.Settings.NameOpacity = v);
        menu.Add(item);

        item = new TextMenu.SubHeader("Chat");
        menu.Add(item);

        item = new TextMenu.Option<string>("New Messages")
            .Add("Chat Messages Only", null!, true)
            .Add("System Messages Only", null!, false)
            .Add("All", "All", false);
        menu.Add(item);
    }
}