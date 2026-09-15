using System.Text;
using MiaoNet.Shared;

namespace MiaoNet.MockClient;

public static class Program
{
    private static readonly List<MockInstance> instances = new();

    private static readonly MockMode[] Modes = Enum.GetValues<MockMode>();
    private const MockMode DefaultMode = MockMode.SingleMap;

    public static void Main()
    {
        int count = AskCount();
        MockMode mode = AskMode();

        PlayerLocation[] locations = MockLocationPool.Distribute(count, mode);
        Console.WriteLine($"Using mode {mode}, {locations.Distinct().Count()} distinct location(s).");

        for (int i = 0; i < count; i++)
        {
            string name = GenerateRandomString(Random.Shared.Next(4, 14));
            Console.WriteLine($"Created client {name} at {locations[i]}");
            instances.Add(new MockInstance(name, locations[i]));
            //Thread.Sleep(500);
        }

        Console.WriteLine("Press enter to exit...");
        Console.ReadLine();
        foreach (MockInstance instance in instances)
        {
            instance.Close(true);
            Console.WriteLine($"Closed {instance.Name}.");
        }
    }

    private static int AskCount()
    {
        Console.Write("Mock client instances count:\n> ");
        return int.Parse(Console.ReadLine()!);
    }

    private static MockMode AskMode()
    {
        Console.WriteLine("Mock mode:");
        for (int i = 0; i < Modes.Length; i++)
        {
            string defaultSuffix = Modes[i] == DefaultMode ? ", default" : string.Empty;
            Console.WriteLine($"  {i + 1}: {Modes[i]}{defaultSuffix}");
        }

        Console.Write("> ");
        return ParseMode(Console.ReadLine());
    }

    private static MockMode ParseMode(string? input)
    {
        string text = input?.Trim() ?? string.Empty;

        if (text.Length == 0)
            return DefaultMode;

        if (int.TryParse(text, out int index) && index >= 1 && index <= Modes.Length)
            return Modes[index - 1];

        if (Enum.TryParse(text, true, out MockMode mode) && Enum.IsDefined(mode))
            return mode;

        Console.WriteLine($"Unknown mode '{text}', falling back to {DefaultMode}.");
        return DefaultMode;
    }

    private static readonly char[] Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();
    private static readonly Random Random = new Random();

    public static string GenerateRandomString(int length = 8)
    {
        var stringBuilder = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            stringBuilder.Append(Chars[Random.Next(Chars.Length)]);
        }
        return stringBuilder.ToString();
    }
}
