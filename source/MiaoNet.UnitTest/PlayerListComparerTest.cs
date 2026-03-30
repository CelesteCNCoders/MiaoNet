#pragma warning disable CA1861

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
        List<PlayerInfo> infos = [
            CreatePlayerInfo("sapcc"),
            CreatePlayerInfo("Ccc"),
            CreatePlayerInfo("AAlice"),
            CreatePlayerInfo("sapcc"),
            CreatePlayerInfo("Admin"),
            CreatePlayerInfo("EmptyPos"),
            CreatePlayerInfo("David"),
            CreatePlayerInfo("voidsd"),
            CreatePlayerInfo("mo_fish")
        ];

        List<MockPlayer> playerList = [
            new MockPlayer(infos[0], new PlayerLocation("Celeste/1a", AreaMode.Normal, "a-01")),
            new MockPlayer(infos[1], new PlayerLocation("Celeste/2a", AreaMode.Normal, "a-01")),
            new MockPlayer(infos[2], new PlayerLocation("Celeste/Farewell", AreaMode.Normal,"j-17")),
            new MockPlayer(infos[3], new PlayerLocation("Celeste/Farewell", AreaMode.Normal,"j-16")),
            new MockPlayer(infos[4], new PlayerLocation("Celeste/Farewell", AreaMode.Normal, "end-golden")),
            new MockPlayer(infos[5], PlayerLocation.Empty),
            new MockPlayer(infos[6], new PlayerLocation("Celeste/1a", AreaMode.Normal, "b-0c")),
            new MockPlayer(infos[7], new PlayerLocation("SpringCollab2020/Expert/ZZ-HeartSide", AreaMode.Normal, "idk-a")),
            new MockPlayer(infos[8], PlayerLocation.Empty),
        ];
        playerList.Sort(new PlayerListEntryComparer());

        CollectionAssert.AreEqual(
            playerList.Select(p => infos.IndexOf(p.PlayerInfo)).ToArray(),
            new int[] { 0, 6, 1, 4, 3, 2, 7, 5, 8 }
        );

        static PlayerInfo CreatePlayerInfo(string name)
            => new(name, string.Empty, string.Empty, Color.White);
    }
}
