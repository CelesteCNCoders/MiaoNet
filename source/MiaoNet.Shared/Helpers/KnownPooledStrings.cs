namespace MiaoNet.Shared;

public static partial class KnownPooledStrings
{
#if DEBUG

    public static IEnumerable<string> All => [];

#else

    public static IEnumerable<string> All =>
        PlayerAnimations
            .Union(PlayerSounds)
            .Append(PlayerSoundParamName)
            .Prepend(string.Empty);

    private static IEnumerable<string> PlayerAnimations => [
        "idle", "runSlow_carry", "fallSlow_carry", "pickUp", "throw", "idleA", "idleB", "idleC", "lookUp",
        "runSlow", "runStumble", "dreamDashIn", "dreamDashOut", "fallSlow", "fallFast", "tiredStill",
        "climbLookBackStart", "climbPush", "climbPull", "fallPose", "faint", "flip", "deadside", "deadup",
        "deaddown", "startStarFly", "fall", "bigFallRecover", "sleep", "bagdown", "asleep", "wakeUp",
        "halfWakeUp", "starMorph", "carryTheoCollapse", "tentacle_grab", "sitDown", "launchRecover",
        "idle_carry", "jumpSlow_carry", "walk", "push", "runFast", "runWind", "dash", "dreamDashLoop",
        "slide", "jumpSlow", "jumpFast", "tired", "wallslide", "climbLookBack", "climbup", "duck",
        "edge", "edgeBack", "fainted", "skid", "dangling", "swimIdle", "swimUp", "swimDown", "starFly",
        "bubble", "bigFall", "spin", "shaking", "hug", "starMorphIdle", "carryTheoWalk", "tentacle_grabbed",
        "tentacle_pull", "tentacle_dangling", "launch"
    ];

    // serialize:
    // 1. remove the "event:" prefix
    // 2. if it starts with "char/madeline", remove it too
    // deserizalie:
    // 1. if it does not start with "/", prepend it with "char/madeline"
    // 2. prepend it with the "event:/" prefix
    private static IEnumerable<string> PlayerSounds => [
        "footstep","landing", "jump", "jump_assisted", "jump_wall_right", "jump_wall_left", "jump_climb_left",
        "jump_climb_right", "jump_super", "jump_superwall", "jump_superslide", "jump_special", "jump_dreamblock",
        "grab", "grab_letgo", "handhold", "wallslide", "duck", "stand", "climb_loop", "climb_ledge", "dash_red_left",
        "dash_red_right", "dash_pink_left", "dash_pink_right", "core_hair_charged", "predeath", "death", "revive",
        "campfire_sit", "campfire_stand", "dreamblock_enter", "dreamblock_travel", "dreamblock_exit",
        "mirrortemple_big_landing", "crystaltheo_lift", "crystaltheo_throw", "theo_collapse", "summit_flytonext",
        "summit_areastart", "summit_sit", "idle_scratch", "idle_sneeze", "idle_crackknuckles", "backpack_drop", "water_in",
        "water_out", "water_dash_in", "water_dash_out", "water_dash_gen", "water_move_shallow", "water_move_general",
        "energy_out_loop", "energy_recharged", "/game/general/assist_screenbottom", "/MiaoNet/player/revive"
    ];

    private const string PlayerSoundParamName = "surface_index";

#endif
}