using FMOD.Studio;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.Cil;

namespace Celeste.Mod.MiaoNet;

public sealed class MiaoNetModule : EverestModule
{
    public static MiaoNetModule Instance { get; private set; } = null!;

    public override Type SettingsType => typeof(MiaoNetModuleSettings);
    public static MiaoNetModuleSettings Settings => (MiaoNetModuleSettings)Instance._Settings;

    public MiaoNetContext MiaoNetContext { get; private set; }

    public static readonly RasterizerState ScissorEnabledRasterizerState
        = new RasterizerState() { ScissorTestEnable = true };

    public static event Action<PlayerLocation>? PlayerLocationChanged;

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
        Everest.Events.Level.OnExit += Level_OnExit;
        Everest.Events.Level.OnLoadLevel += Level_OnLoadLevel;
        IL.Celeste.Level.Update += Level_Update;
    }

    public override void Unload()
    {
        MiaoNetContext.Disconnect();
        Everest.Events.Level.OnCreatePauseMenuButtons -= Level_OnCreatePauseMenuButtons;
        IL.Monocle.Engine.Update -= Engine_Update;
        IL.Monocle.Engine.RenderCore -= Engine_RenderCore;
        Everest.Events.Level.OnExit -= Level_OnExit;
        Everest.Events.Level.OnLoadLevel -= Level_OnLoadLevel;
        IL.Celeste.Level.Update -= Level_Update;
    }

    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot)
    {
        MenuMiaoNetOptions.BuildMenu(menu, inGame);
        CreateModMenuSectionKeyBindings(menu, inGame, snapshot);
    }

    private static void Engine_Update(ILContext il)
    {
        ILCursor cur = new(il);
        cur.EmitDelegate(static () => Instance.MiaoNetContext.Update());
    }

    private static void Engine_RenderCore(ILContext il)
    {
        ILCursor cur = new(il);
        // evil render position
        cur.Index = cur.Instrs.Count - 1;
        cur.EmitDelegate(static () => Instance.MiaoNetContext.Render());
    }

    private static void Level_Update(ILContext il)
    {
        // TODO will there be a mod that opens debug map?
        ILCursor cur = new(il);
        cur.GotoNext(MoveType.After,
            ins => ins.MatchLdarg0(),
            ins => ins.MatchLdfld<Level>(nameof(Level.Session)),
            ins => ins.MatchLdfld<Session>(nameof(Session.Area)),
            ins => ins.MatchLdcI4(1),
            ins => ins.MatchNewobj<Editor.MapEditor>(),
            ins => ins.MatchCall<Engine>($"set_{nameof(Engine.Scene)}")
        );
        cur.EmitLdarg0();
        cur.EmitDelegate(
            static (Level level) => PlayerLocationChanged?.Invoke(
                new PlayerLocation(level.Session.Area.SID, string.Empty)
            )
        );
    }

    private void Level_OnLoadLevel(Level level, Player.IntroTypes playerIntro, bool isFromLoader)
        => PlayerLocationChanged?.Invoke(PlayerLocation.FetchFrom(level.Session));

    private static void Level_OnExit(Level level, LevelExit exit, LevelExit.Mode mode, Session session, HiresSnow snow)
        => PlayerLocationChanged?.Invoke(PlayerLocation.Empty);

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
            const string ButtonBackAudio = SFX.ui_main_button_back;
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