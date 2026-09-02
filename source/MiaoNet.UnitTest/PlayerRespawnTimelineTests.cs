using Celeste.Mod.MiaoNet;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class PlayerRespawnTimelineTests
{
    private static readonly PlayerLocation LevelLocation = new(
        "Celeste/1-ForsakenCity",
        AreaMode.Normal,
        "1"
    );

    [TestMethod]
    public void OrdinaryDeathRespawnUsesCurrentMapTimeline()
    {
        Assert.IsTrue(PlayerRespawnTimeline.CanEmitRespawn(
            LevelLocation,
            LevelLocation,
            PlayerStateFlags.Dead
        ));
    }

    [TestMethod]
    public void GoldenRestartCannotRespawnInEmptyLocationTimeline()
    {
        Assert.IsFalse(PlayerRespawnTimeline.CanEmitRespawn(
            PlayerLocation.Empty,
            LevelLocation,
            PlayerStateFlags.Dead
        ));
    }

    [TestMethod]
    public void MissingPlayerStateWaitsForLocationBarrier()
    {
        Assert.IsFalse(PlayerRespawnTimeline.CanEmitRespawn(
            LevelLocation,
            LevelLocation,
            null
        ));
    }

    [TestMethod]
    public void AliveStateDoesNotProduceRespawnEvent()
    {
        Assert.IsFalse(PlayerRespawnTimeline.CanEmitRespawn(
            LevelLocation,
            LevelLocation,
            PlayerStateFlags.None
        ));
    }
}
