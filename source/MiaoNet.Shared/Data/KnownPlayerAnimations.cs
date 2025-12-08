using System.Collections.Frozen;
using System.Collections.Immutable;

namespace MiaoNet.Shared;

// TODO known string registering
public static class KnownPlayerAnimations
{
    public static FrozenDictionary<string, int> StringToID { get; }
    public static FrozenDictionary<int, string> IDToString { get; }

    static KnownPlayerAnimations()
    {
        string[] knownAnimationIDStrings =
        [
            "idle", "runSlow_carry", "fallSlow_carry", "pickUp", "throw", "idleA", "idleB", "idleC", "lookUp",
            "runSlow", "runStumble", "dreamDashIn", "dreamDashOut", "fallSlow", "fallFast", "tiredStill", "climbLookBackStart",
            "climbPush", "climbPull", "fallPose", "faint", "flip", "deadside", "deadup", "deaddown", "startStarFly",
            "fall", "bigFallRecover", "sleep", "bagdown", "asleep", "wakeUp", "halfWakeUp", "starMorph", "carryTheoCollapse",
            "tentacle_grab", "sitDown", "launchRecover", "idle_carry", "jumpSlow_carry", "walk", "push", "runFast", "runWind",
            "dash", "dreamDashLoop", "slide", "jumpSlow", "jumpFast", "tired", "wallslide", "climbLookBack", "climbup", "duck",
            "edge", "edgeBack", "fainted", "skid", "dangling", "swimIdle", "swimUp", "swimDown", "starFly", "bubble", "bigFall",
            "spin", "shaking", "hug", "starMorphIdle", "carryTheoWalk", "tentacle_grabbed", "tentacle_pull","tentacle_dangling",
            "launch"
        ];
        StringToID = knownAnimationIDStrings
            .Select((s, n) => new KeyValuePair<string, int>(s, n))
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        IDToString = knownAnimationIDStrings
            .Select((s, n) => new KeyValuePair<int, string>(n, s))
            .ToFrozenDictionary();
    }
}