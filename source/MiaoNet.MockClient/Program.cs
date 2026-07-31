using System.Text;

namespace MiaoNet.MockClient;

public static class Program
{
    private static readonly List<MockInstance> instances = new();

    public static void Main()
    {
        Console.Write("Mock client instances count:\n> ");
        int count = int.Parse(Console.ReadLine()!);

        for (int i = 0; i < count; i++)
        {
            string name = GenerateRandomString(Random.Shared.Next(4, 14));
            Console.WriteLine($"Created client {name}");
            instances.Add(new MockInstance(name));
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
