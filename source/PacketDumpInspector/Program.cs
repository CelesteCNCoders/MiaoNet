using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using MiaoNet.Shared;

namespace PacketDumpInspector;

public sealed class Program
{
    public static void Main()
    {
        var ctx = new InspectorContext();
        JsonSerializerOptions options = new()
        {
            IncludeFields = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            Converters = { new JsonStringEnumConverter() },
            WriteIndented = true
        };

        Console.Write("File name:\n> ");
        string fileName = Console.ReadLine()!.Trim('\"');
        var bytes = File.ReadAllBytes(fileName);
        ReadOnlySpan<byte> span = bytes;
        while (true)
        {
            RefBinaryReader reader = new RefBinaryReader(span);
            ushort size = reader.ReadUInt16();
            ushort type = reader.ReadUInt16();
            var readHandler = PacketRegistry.GetPacketReader(type);
            var packet = readHandler(ref reader, ctx);
            var text = JsonSerializer.Serialize<object>(packet, options);
            Console.WriteLine($"==={packet.GetType().FullName}===");
            Console.WriteLine(text);
            span = span[(4 + size)..];
        }
    }

    private sealed class InspectorContext : IPacketSerializationContext
    {
        public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
    }
}
