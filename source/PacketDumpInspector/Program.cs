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
            byte type = reader.ReadByte();
            byte flags = reader.ReadByte();
            PacketEnvelope envelope = PacketEnvelope.ReadOptional(ref reader, (PacketEnvelopeFlags)flags);
            var readHandler = PacketRegistry.GetPacketReader(type);
            var packet = readHandler(ref reader, ctx);
            var text = JsonSerializer.Serialize<object>(packet, options);
            Console.WriteLine($"==={packet.GetType().FullName}===");
            string sender = envelope.HasSender ? envelope.SenderPlayerID.ToString() : "-";
            string request = envelope.HasRequestID ? envelope.RequestID.ToString() : "-";
            Console.WriteLine($"envelope: sender={sender}, request={request}");
            Console.WriteLine(text);
            span = span[(Connection.PacketHeaderSize + size)..];
        }
    }

    private sealed class InspectorContext : IPacketSerializationContext
    {
        public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
    }
}
