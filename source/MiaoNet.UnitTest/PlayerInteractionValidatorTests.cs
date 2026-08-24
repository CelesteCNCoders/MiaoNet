using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class PlayerInteractionValidatorTests
{
    [TestMethod]
    public void GrabRequiresSameSyncScopeAndBothPlayersOptedIn()
    {
        ServerChannel channel = new(0, new ChannelInfo("test"));
        ServerPlayer source = CreatePlayer(channel, 1);
        ServerPlayer target = CreatePlayer(channel, 2);

        Assert.IsTrue(PlayerInteractionValidator.CanGrab(source, target));

        target.GlobalFlags = PlayerGlobalFlags.None;
        Assert.IsFalse(PlayerInteractionValidator.CanGrab(source, target));

        target.GlobalFlags = PlayerGlobalFlags.Interactions;
        target.Location = new PlayerLocation("Other/Map", AreaMode.Normal, "room");
        Assert.IsFalse(PlayerInteractionValidator.CanGrab(source, target));
    }

    [TestMethod]
    public void ReleaseForceMustBeFinite()
    {
        Assert.IsTrue(PlayerInteractionValidator.IsValidReleaseForce(new Vector2(1, -1)));
        Assert.IsFalse(PlayerInteractionValidator.IsValidReleaseForce(new Vector2(float.NaN, 0)));
        Assert.IsFalse(PlayerInteractionValidator.IsValidReleaseForce(new Vector2(0, float.PositiveInfinity)));
    }

    private static ServerPlayer CreatePlayer(ServerChannel channel, int id)
        => new(channel, id, new PlayerInfo(id, $"p{id}", string.Empty, string.Empty, Color.White))
        {
            Location = new PlayerLocation("Test/Map", AreaMode.Normal, "room"),
            GlobalFlags = PlayerGlobalFlags.Interactions
        };
}
