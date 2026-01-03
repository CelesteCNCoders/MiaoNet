namespace MiaoNet.Shared;

public static partial class KnownPooledStrings
{
#if !INSPECTOR

#if DEBUG

    public static IEnumerable<string> All => [];

#elif RELEASE

    public static IEnumerable<string> All =>
        PlayerAnimations.Prepend(string.Empty);

#endif

#else

    public static IEnumerable<string> Empty => [];

    public static IEnumerable<string> All =>
        PlayerAnimations.Prepend(string.Empty);

#endif

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
}