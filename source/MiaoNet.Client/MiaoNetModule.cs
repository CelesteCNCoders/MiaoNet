using FMOD.Studio;
using MonoMod.Cil;

namespace Celeste.Mod.MiaoNet;

public sealed class MiaoNetModule : EverestModule
{
    public static MiaoNetModule Instance { get; private set; } = null!;

    public override Type SettingsType => typeof(MiaoNetModuleSettings);
    public static MiaoNetModuleSettings Settings => (MiaoNetModuleSettings)Instance._Settings;

    public MiaoNetContext MiaoNetContext { get; private set; }

    public MiaoNetModule()
    {
        MiaoNetContext = new();
    }

    public override void Load()
    {
        Instance = this;
        Everest.Events.Level.OnCreatePauseMenuButtons += Level_OnCreatePauseMenuButtons;
        IL.Monocle.Engine.Update += Engine_Update;
        IL.Monocle.Engine.RenderCore += Engine_RenderCore;
        On.Celeste.Level.LoadLevel += Level_LoadLevel;
    }

    public override void Unload()
    {
        MiaoNetContext.Disconnect();
        Everest.Events.Level.OnCreatePauseMenuButtons -= Level_OnCreatePauseMenuButtons;
        IL.Monocle.Engine.Update -= Engine_Update;
        IL.Monocle.Engine.RenderCore -= Engine_RenderCore;
    }

    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot)
    {
        MenuMiaoNetOptions.BuildMenu(menu, inGame);
    }

    private static void Engine_Update(ILContext il)
    {
        ILCursor cur = new(il);
        cur.EmitDelegate(static () =>
        {
            var ctx = Instance.MiaoNetContext;
            ctx.Update();
        });
    }

    private static void Engine_RenderCore(ILContext il)
    {
        ILCursor cur = new(il);
        cur.Index = cur.Instrs.Count - 1;
        cur.EmitDelegate(static () =>
        {
            var ctx = Instance.MiaoNetContext;
            ctx.Render();
        });
    }

    private static void Level_LoadLevel(
        On.Celeste.Level.orig_LoadLevel orig,
        Level self,
        Player.IntroTypes playerIntro, bool isFromLoader
    )
    {
        orig(self, playerIntro, isFromLoader);
        Instance.MiaoNetContext.OnPlayerMapChanged(self, self.Session.Area.SID, self.Session.Level);
    }

    private static void Level_OnCreatePauseMenuButtons(Level level, TextMenu menu, bool minimal)
    {
        TextMenu.Item item = new TextMenu.Button("MiaoNet");
        item.Pressed(() =>
        {
            menu.RemoveSelf();
            level.PauseMainMenuOpen = false;
            int returnIndex = menu.IndexOf(item);

            level.Paused = true;
            bool oldAllowHudHide = level.AllowHudHide;
            level.AllowHudHide = false;
            TextMenu options = new TextMenu();
            MenuMiaoNetOptions.BuildHeader(options);
            MenuMiaoNetOptions.BuildMenu(options, true);
            const string ButtonBackAudio = "event:/ui/main/button_back";
            options.OnESC = (options.OnCancel = () =>
            {
                Audio.Play(ButtonBackAudio);
                level.AllowHudHide = oldAllowHudHide;
                level.Pause(returnIndex, minimal);
                options.Close();
            });
            options.OnPause = () =>
            {
                Audio.Play(ButtonBackAudio);
                level.AllowHudHide = oldAllowHudHide;
                level.Paused = false;
                options.Close();
            };
            level.Add(options);
        });
        // TODO 444444444444444444444
        menu.Insert(4, item);
    }
}