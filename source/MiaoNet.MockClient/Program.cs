using System.Text;

namespace MiaoNet.MockClient;

public sealed class Program
{
    public static void Main()
    {
        Console.Write("Mock client instances count:\n> ");
        int count = int.Parse(Console.ReadLine()!);

        for (int i = 0; i < count; i++)
        {
            string name = GenerateRandomString();
            Console.WriteLine($"Created client {name}");
            _ = new MockInstance(name);
        }

        Console.WriteLine("Press enter to exit...");
        Console.ReadLine();
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
