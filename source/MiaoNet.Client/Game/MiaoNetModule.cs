using FMOD.Studio;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.Cil;
using MonoMod.ModInterop;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace Celeste.Mod.MiaoNet;

public sealed class MiaoNetModule : EverestModule
{
    private static bool seenOverworld;

    public const string LeaderFollowersDirtyField = "mn_followersDirty";

    public static MiaoNetModule Instance { get; private set; } = null!;

    public override Type SettingsType => typeof(MiaoNetModuleSettings);
    public static MiaoNetModuleSettings Settings => (MiaoNetModuleSettings)Instance._Settings;

    private static readonly DetourConfig RootConfig = new("MiaoNet");
    private static readonly DetourConfig RootBeforeAllConfig = new("MiaoNet.BeforeAll", before: ["*"]);

    public MiaoNetContext MiaoNetContext { get; private set; }

    // TODO need we use scissors to render chats?
    /*
    public static readonly RasterizerState ScissorEnabledRasterizerState
        = new RasterizerState() { ScissorTestEnable = true };
    */

    // TODO this is ugly
    public static Vector2? NextPlayerSpawnPosition { get; set; }

    public delegate void PlayerLocationChangedHandler(PlayerLocation location, bool forceFullChange);
    public static event PlayerLocationChangedHandler? PlayerLocationChanged;

    public MiaoNetModule()
    {
        MiaoNetContext = new();
    }

    public override void Load()
    {
        Instance = this;
        using (new DetourConfigContext(RootConfig).Use())
        {
            Everest.Events.Level.OnCreatePauseMenuButtons += Level_OnCreatePauseMenuButtons;
            IL.Monocle.Engine.Update += Engine_Update;
            IL.Monocle.Engine.RenderCore += Engine_RenderCore;
            Everest.Events.Level.OnExit += Level_OnExit;
            Everest.Events.Level.OnLoadLevel += Level_OnLoadLevel;
            IL.Celeste.Level.Update += Level_Update;
            SpriteIDTracker.Load();
            IL.Celeste.Leader.GainFollower += ILHook_LeaderFollowersMarkDirty;
            IL.Celeste.Leader.LoseFollower += ILHook_LeaderFollowersMarkDirty;
            IL.Celeste.Leader.LoseFollowers += ILHook_LeaderFollowersMarkDirty;
            On.Celeste.Overworld.Begin += Overworld_Begin;
            On.Celeste.Player.Added += Player_Added;
            Everest.Events.LevelLoader.OnLoadingThread += LevelLoader_OnLoadingThread;
        }
        using (new DetourConfigContext(RootBeforeAllConfig).Use())
        {
            On.Celeste.PlayerSprite.ctor += PlayerSprite_ctor;
        }

        typeof(SpeedrunToolInterop).ModInterop();
        SpeedrunToolInterop.AddReturnSameObjectProcessor?.Invoke(t => t.Assembly == typeof(MiaoNetContext).Assembly);
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
        SpriteIDTracker.Unload();
        IL.Celeste.Leader.GainFollower -= ILHook_LeaderFollowersMarkDirty;
        IL.Celeste.Leader.LoseFollower -= ILHook_LeaderFollowersMarkDirty;
        IL.Celeste.Leader.LoseFollowers -= ILHook_LeaderFollowersMarkDirty;
        On.Celeste.Overworld.Begin -= Overworld_Begin;
        On.Celeste.Player.Added -= Player_Added;
        Everest.Events.LevelLoader.OnLoadingThread -= LevelLoader_OnLoadingThread;

        On.Celeste.PlayerSprite.ctor -= PlayerSprite_ctor;
    }

    public override void OnInputInitialize()
    {
        InitializeButton(Settings.ChatButton);
        InitializeButton(Settings.PlayerListButton);
        foreach (var item in Settings.EmoteButtons)
            InitializeButton(item);
    }

    public static void InitializeButton(ButtonBinding buttonBinding)
    {
        buttonBinding.Button = new VirtualButton(buttonBinding.Binding, Input.Gamepad, 0.08f, 0.2f);
        buttonBinding.Button.AutoConsumeBuffer = true;
    }

    private static void ILHook_LeaderFollowersMarkDirty(ILContext il)
    {
        // or we can just read Followers._version evilly...
        ILCursor cur = new(il);
        cur.EmitLdarg0();
        cur.EmitDelegate(static (Leader leader) =>
        {
            if (leader.Entity is not Player)
                return;
            DynamicData.For(leader).Set(LeaderFollowersDirtyField, true);
        });
    }

    private static void PlayerSprite_ctor(On.Celeste.PlayerSprite.orig_ctor orig, PlayerSprite self, PlayerSpriteMode mode)
    {
        // CelesteNet do this, same for us for compatibility
        orig(self, mode & (PlayerSpriteMode)~(1 << 31));
    }

    private static void Player_Added(On.Celeste.Player.orig_Added orig, Player self, Scene scene)
    {
        orig(self, scene);
        if (NextPlayerSpawnPosition.HasValue)
        {
            self.Position = NextPlayerSpawnPosition.Value;
            NextPlayerSpawnPosition = null;
        }
    }

    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot)
    {
        MenuMiaoNetOptions.BuildMenu(menu, inGame);
        MenuMiaoNetOptions.AddKeyBindingsSection(menu, inGame);
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
        // TODO will there be a mod that opens debug map else where?
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
                new PlayerLocation(level.Session.Area.SID, level.Session.Area.Mode, string.Empty),
                false
            )
        );
    }

    private static void Level_OnLoadLevel(Level level, Player.IntroTypes playerIntro, bool isFromLoader)
        => PlayerLocationChanged?.Invoke(PlayerLocation.FetchFrom(level.Session), isFromLoader);

    private static void LevelLoader_OnLoadingThread(Level level)
    {
        level.Add(new GhostRenderLayerEntity(isHigh: false));
        level.Add(new GhostRenderLayerEntity(isHigh: true));
    }

    private static void Overworld_Begin(On.Celeste.Overworld.orig_Begin orig, Overworld self)
    {
        orig(self);
        if (!seenOverworld)
        {
            if (Settings.ConnectOnGameStart)
            {
                Entity entity = new();
                Alarm.Set(entity, 4f, () => { Instance.MiaoNetContext.Connect(); entity.RemoveSelf(); });
                self.Add(entity);
            }
            seenOverworld = true;
        }
    }

    private static void Level_OnExit(Level level, LevelExit exit, LevelExit.Mode mode, Session session, HiresSnow snow)
        => PlayerLocationChanged?.Invoke(PlayerLocation.Empty, true);

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