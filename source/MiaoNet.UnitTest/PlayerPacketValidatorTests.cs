using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class PlayerPacketValidatorTests
{
    [TestMethod]
    [DataRow(0, true)]
    [DataRow(12, true)]
    [DataRow(13, false)]
    public void InitialState_FollowerBoundary_IsEnforced(int count, bool expected)
    {
        PlayerState state = CreatePlayerState(count);

        Assert.AreEqual(expected, PlayerPacketValidator.HasValidFollowerCount(state));
    }

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(12, true)]
    [DataRow(13, false)]
    public void DeltaFollowerInitials_Boundary_IsEnforced(int count, bool expected)
    {
        PlayerStateDelta delta = CreateDelta(PlayerStateDelta.FrameFlags.HasFollowerInitials);
        delta.FollowerInitials = new FollowerInfo[count];

        Assert.AreEqual(expected, PlayerPacketValidator.HasValidFollowerCount(delta));
    }

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(12, true)]
    [DataRow(13, false)]
    public void DeltaFollowerDeltas_Boundary_IsEnforced(int count, bool expected)
    {
        PlayerStateDelta delta = CreateDelta(PlayerStateDelta.FrameFlags.HasFollowerDeltas);
        delta.FollowerDeltas = new FollowerInfoDelta[count];

        Assert.AreEqual(expected, PlayerPacketValidator.HasValidFollowerCount(delta));
    }

    private static PlayerState CreatePlayerState(int followerCount)
        => new()
        {
            Position = default,
            Animation = string.Empty,
            AnimationFrame = 0,
            Scale = default,
            StateFlags = PlayerStateFlags.None,
            Dashes = 0,
            DeltaTime = 0,
            PlayerSpriteMode = default,
            HoldableInfo = default,
            FollowerInfos = new FollowerInfo[followerCount],
            WindDirection = default,
        };

    private static PlayerStateDelta CreateDelta(PlayerStateDelta.FrameFlags flags)
        => new(default, string.Empty, 0, default, flags, PlayerStateFlags.None);
}
