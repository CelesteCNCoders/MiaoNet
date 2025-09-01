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

    private void Engine_Update(ILContext il)
    {
        ILCursor cur = new(il);
        cur.EmitDelegate(static () =>
        {
            var ctx = Instance.MiaoNetContext;
            ctx.Update();
        });
    }

    private void Engine_RenderCore(ILContext il)
    {
        ILCursor cur = new(il);
        cur.Index = cur.Instrs.Count - 1;
        cur.EmitDelegate(static () =>
        {
            var ctx = Instance.MiaoNetContext;
            ctx.Render();
        });
    }

    private void Level_OnCreatePauseMenuButtons(Level level, TextMenu menu, bool minimal)
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
            options.OnESC = (options.OnCancel = () =>
            {
                Audio.Play("event:/ui/main/button_back");
                level.AllowHudHide = oldAllowHudHide;
                level.Pause(returnIndex, minimal);
                options.Close();
            });
            options.OnPause = () =>
            {
                Audio.Play("event:/ui/main/button_back");
                level.AllowHudHide = oldAllowHudHide;
                level.Paused = false;
                options.Close();
            };
            level.Add(options);
        });
        menu.Insert(4, item);
    }
}