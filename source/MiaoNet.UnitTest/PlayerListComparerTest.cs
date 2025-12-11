#pragma warning disable CA1861

using MiaoNet.Server.Primitives;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

public sealed class MockPlayer : IPlayerListEntry
{
    public PlayerInfo PlayerInfo { get; set; }

    public PlayerLocation Location { get; set; }

    public MockPlayer(PlayerInfo playerInfo, PlayerLocation location)
    {
        PlayerInfo = playerInfo;
        Location = location;
    }
}

[TestClass]
public class PlayerListComparerTest
{
    [TestMethod]
    public void TestCompare()
    {
        List<MockPlayer> playerList = [
            new MockPlayer(new PlayerInfo(0, "sapcc"), new PlayerLocation("Celeste/1a", AreaMode.Normal, "a-01")),
            new MockPlayer(new PlayerInfo(1, "Ccc"), new PlayerLocation("Celeste/2a", AreaMode.Normal, "a-01")),
            new MockPlayer(new PlayerInfo(2, "AAlice"), new PlayerLocation("Celeste/Farewell", AreaMode.Normal,"j-17")),
            new MockPlayer(new PlayerInfo(3, "sapcc"), new PlayerLocation("Celeste/Farewell", AreaMode.Normal,"j-16")),
            new MockPlayer(new PlayerInfo(4, "Admin"), new PlayerLocation("Celeste/Farewell", AreaMode.Normal, "end-golden")),
            new MockPlayer(new PlayerInfo(5, "EmptyPos"), PlayerLocation.Empty),
            new MockPlayer(new PlayerInfo(6, "David"), new PlayerLocation("Celeste/1a", AreaMode.Normal, "b-0c")),
            new MockPlayer(new PlayerInfo(7, "voidsd"), new PlayerLocation("SpringCollab2020/Expert/ZZ-HeartSide", AreaMode.Normal, "idk-a")),
            new MockPlayer(new PlayerInfo(8, "mo_fish"), PlayerLocation.Empty),
        ];
        playerList.Sort(new PlayerListEntryComparer());

        CollectionAssert.AreEqual(
            playerList.Select(p => p.PlayerInfo.ID).ToArray(),
            new int[] { 0, 6, 1, 4, 3, 2, 7, 5, 8 }
        );
    }
}
