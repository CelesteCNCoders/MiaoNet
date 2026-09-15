using MiaoNet.Shared;

namespace MiaoNet.MockClient;

public static class MockLocationPool
{
    public const int MaxInstancesPerMap = 8;

    public static readonly PlayerLocation SingleMapLocation =
        new("Celeste/LostLevels", AreaMode.Normal, "intro-00-past");

    private static readonly (string SidSuffix, string? Normal, string? BSide, string? CSide)[] OfficialMaps =
    [
        ("0-Intro", "-1", null, null),
        ("1-ForsakenCity", "1", "00", "00"),
        ("2-OldSite", "0", "00", "00"),
        ("3-CelestialResort", "00-a", "00", "00"),
        ("4-GoldenRidge", "a-00", "a-00", "00"),
        ("5-MirrorTemple", "a-00", "a-00", "00"),
        ("6-Reflection", "00", "a-00", "00"),
        ("7-Summit", "a-00-intro", "a-00-intro", "01"),
        ("8-Epilogue", "bridge", null, null),
        ("9-Core", "00", "00", "00"),
        ("LostLevels", "a-00", null, null),
    ];

    private static readonly PlayerLocation[] OfficialLocations = BuildOfficialLocations();

    private const string NonexistentSidPrefix = "MiaoNet/Nonexistent/";

    private static PlayerLocation[] BuildOfficialLocations()
    {
        List<PlayerLocation> locations = new(OfficialMaps.Length * 3);
        foreach ((string sidSuffix, string? normalRoom, string? bSideRoom, string? cSideRoom) in OfficialMaps)
        {
            string sid = $"Celeste/{sidSuffix}";
            AddLocation(locations, sid, AreaMode.Normal, normalRoom);
            AddLocation(locations, sid, AreaMode.BSide, bSideRoom);
            AddLocation(locations, sid, AreaMode.CSide, cSideRoom);
        }
        return [.. locations];

        static void AddLocation(List<PlayerLocation> locations, string sid, AreaMode areaMode, string? room)
        {
            if (room is not null)
                locations.Add(new PlayerLocation(sid, areaMode, room));
        }
    }

    public static PlayerLocation CreateNonexistentLocation(int index)
    {
        AreaMode areaMode = (AreaMode)(index % 3);
        char sideCharacter = (char)('A' + (char)areaMode);
        string sid = $"{NonexistentSidPrefix}{sideCharacter}-{index / 3:D3}";
        return new PlayerLocation(sid, areaMode, $"{(char)('a' + (char)areaMode)}-00");
    }

    public static IReadOnlyList<PlayerLocation> All { get; } =
        [.. OfficialLocations, .. Enumerable.Range(0, 30).Select(CreateNonexistentLocation)];

    public static PlayerLocation[] Distribute(int count, MockMode mode)
    {
        if (mode is MockMode.Scattered)
        {
            PlayerLocation[] locations = [.. All];
            Random.Shared.Shuffle(locations);

            int[] assigned = new int[locations.Length];
            int remaining = count;

            int spread = Math.Min(remaining, locations.Length);
            for (int i = 0; i < spread; i++)
            {
                assigned[i] = 1;
                remaining--;
            }

            List<int> available = new(locations.Length);
            for (int i = 0; i < locations.Length; i++)
            {
                if (assigned[i] < MaxInstancesPerMap)
                    available.Add(i);
            }

            while (remaining > 0 && available.Count > 0)
            {
                int slot = Random.Shared.Next(available.Count);
                int index = available[slot];
                assigned[index]++;
                remaining--;
                if (assigned[index] >= MaxInstancesPerMap)
                    available.RemoveAt(slot);
            }

            while (remaining > 0)
            {
                for (int i = 0; i < locations.Length && remaining > 0; i++)
                {
                    assigned[i]++;
                    remaining--;
                }
            }

            PlayerLocation[] result = new PlayerLocation[count];
            int cursor = 0;
            for (int i = 0; i < locations.Length; i++)
            {
                for (int j = 0; j < assigned[i]; j++)
                    result[cursor++] = locations[i];
            }

            return result;
        }
        else
        {
            PlayerLocation[] single = new PlayerLocation[count];
            Array.Fill(single, SingleMapLocation);
            return single;
        }
    }
}
