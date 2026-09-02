using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

internal static class WatchValidatorTestHelper
{
    public static bool IsValidState(PlayerLocation location, WatchEntityState state)
        => WatchPacketValidator.IsValid(new WatchSceneSnapshot(location, 0, [], [state]));

    public static bool IsValidEvent(PlayerLocation location, WatchEntityEvent entityEvent)
        => WatchPacketValidator.IsValid(new WatchSceneDelta(
            1,
            location,
            [],
            [],
            false,
            WatchEntityStateMode.None,
            [],
            [entityEvent]
        ));
}
