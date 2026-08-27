using MiaoNet.Server;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public sealed class PlayerFrameRoutingTests
{
    private static readonly PlayerMapLocation Map = new("Celeste/1-ForsakenCity", AreaMode.Normal);

    [TestMethod]
    public void OnlyActiveTargetSessionsReceiveCameraFrames()
    {
        WatchSession inactive = new(1, 10, 20, Map, 30);
        WatchSession active = new(2, 11, 20, Map, 31);
        active.Activate(0);
        WatchSession resyncing = new(3, 12, 20, Map, 32);
        resyncing.Activate(0);
        Assert.AreEqual(WatchSequenceResult.Gap, resyncing.AcceptSequence(2));
        WatchSession wrongMap = new(
            4,
            13,
            20,
            new PlayerMapLocation("Celeste/2-OldSite", AreaMode.Normal),
            33
        );
        wrongMap.Activate(0);
        WatchSession[] sessions = [inactive, active, resyncing, wrongMap];

        Assert.IsFalse(PlayerFrameRouting.IsActiveWatcher(sessions, inactive.WatcherID, Map));
        Assert.IsTrue(PlayerFrameRouting.IsActiveWatcher(sessions, active.WatcherID, Map));
        Assert.IsTrue(PlayerFrameRouting.IsActiveWatcher(sessions, resyncing.WatcherID, Map));
        Assert.IsFalse(PlayerFrameRouting.IsActiveWatcher(sessions, wrongMap.WatcherID, Map));
        Assert.IsFalse(PlayerFrameRouting.IsActiveWatcher(sessions, 99, Map));
    }

    [TestMethod]
    public void CameraIsRemovedWithoutChangingOtherFrameState()
    {
        FollowerInfo[] followers =
        [
            new(FollowerType.Key, "key", "idle", 7, new Vector2S(2, 3)),
        ];
        PlayerStateDelta.FrameFlags flags =
            PlayerStateDelta.FrameFlags.DashesChange
            | PlayerStateDelta.FrameFlags.HasHoldable
            | PlayerStateDelta.FrameFlags.HasFollowerInitials
            | PlayerStateDelta.FrameFlags.HasWindDirection
            | PlayerStateDelta.FrameFlags.HasCameraPosition;
        PlayerStateDelta source = new(
            new Vector2(11f, 21f),
            "runFast",
            4,
            new Vector2(-1f, 1f),
            flags,
            PlayerStateFlags.Dashing
        )
        {
            Dashes = 1,
            DashDirection = 3,
            HoldableInfo = new(HoldableType.Theo, new Vector2(4f, 5f)),
            FollowerInitials = followers,
            WindDirection = new Vector2(6f, 7f),
            CameraPosition = new Vector2(130f, 460f),
        };
        PacketPlayerFrame original = new(7, 11, source);

        PacketPlayerFrame result = PlayerFrameRouting.CreateWithoutCamera(original);
        PlayerStateDelta stripped = result.StateDelta!;

        Assert.AreNotSame(original, result);
        Assert.IsTrue(original.StateDelta!.HasCameraPosition);
        Assert.IsFalse(stripped.HasCameraPosition);
        Assert.AreEqual(flags & ~PlayerStateDelta.FrameFlags.HasCameraPosition, stripped.Flags);
        Assert.AreEqual(source.Position, stripped.Position);
        Assert.AreEqual(source.Animation, stripped.Animation);
        Assert.AreEqual(source.AnimationFrame, stripped.AnimationFrame);
        Assert.AreEqual(source.Scale, stripped.Scale);
        Assert.AreEqual(source.StateFlags, stripped.StateFlags);
        Assert.AreEqual(source.Dashes, stripped.Dashes);
        Assert.AreEqual(source.DashDirection, stripped.DashDirection);
        Assert.AreEqual(source.HoldableInfo, stripped.HoldableInfo);
        Assert.AreSame(followers, stripped.FollowerInitials);
        Assert.AreEqual(source.WindDirection, stripped.WindDirection);
        Assert.AreEqual(Vector2.Zero, stripped.CameraPosition);
    }

    [TestMethod]
    public void FrameWithoutCameraUsesExistingPacket()
    {
        PacketPlayerFrame packet = new(1, 1, new PlayerStateDelta(
            Vector2.Zero,
            string.Empty,
            0,
            Vector2.One,
            PlayerStateDelta.FrameFlags.None,
            PlayerStateFlags.None
        ));

        Assert.AreSame(packet, PlayerFrameRouting.CreateWithoutCamera(packet));
    }

    [TestMethod]
    public void CameraRemovalPreservesFollowerDeltas()
    {
        FollowerInfoDelta[] followerDeltas =
        [
            new("spin", 8, new Vector2S(5, 6)),
        ];
        PacketPlayerFrame packet = new(1, 1, new PlayerStateDelta(
            new Vector2(1f, 2f),
            "idle",
            3,
            Vector2.One,
            PlayerStateDelta.FrameFlags.HasFollowerDeltas
                | PlayerStateDelta.FrameFlags.HasCameraPosition,
            PlayerStateFlags.None
        )
        {
            FollowerDeltas = followerDeltas,
            CameraPosition = new Vector2(7f, 8f),
        });

        PlayerStateDelta stripped = PlayerFrameRouting.CreateWithoutCamera(packet).StateDelta!;

        Assert.IsTrue(stripped.HasFollowerDeltas);
        Assert.AreSame(followerDeltas, stripped.FollowerDeltas);
        Assert.IsFalse(stripped.HasCameraPosition);
    }
}
