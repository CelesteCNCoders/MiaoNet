using System.Diagnostics;
using System.Text.Json;
using MiaoNet.Shared;

namespace PacketDumpInspector;

public sealed class Program
{
    public static void Main()
    {
        Console.Write("Input base64 string: \n-> ");
        string base64 = Console.ReadLine()!;

        byte[] bytes = Convert.FromBase64String(base64);
        string message = $"Array size = {bytes.Length}";
        Debug.WriteLine(message);
        Console.WriteLine(message);
        RefBinaryReader reader = new(bytes);

        var ctx = new InspectorContext();
        var pck = reader.Read<PacketPlayerMapChangedResponse, IPacketSerializationContext>(ctx);

        Console.WriteLine(JsonSerializer.Serialize(pck, new JsonSerializerOptions() { WriteIndented = true, IncludeFields = true }));
    }
}

public sealed class InspectorContext : IPacketSerializationContext
{
    public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
}