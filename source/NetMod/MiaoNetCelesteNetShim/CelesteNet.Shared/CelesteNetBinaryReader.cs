using System.Text;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet;

public class CelesteNetBinaryReader : BinaryReader
{
    public DataContext Data { get; }

    public CelesteNetBinaryReader(DataContext data, Stream input, bool leaveOpen = false)
        : base(input, Encoding.UTF8, leaveOpen)
    {
        Data = data;
    }

    public uint Read7BitEncodedUInt() => unchecked((uint)Read7BitEncodedInt());
    public virtual Vector2 ReadVector2() => new(ReadSingle(), ReadSingle());
    public virtual Color ReadColor() => new(ReadByte(), ReadByte(), ReadByte(), ReadByte());
    public virtual Color ReadColorNoA() => new(ReadByte(), ReadByte(), ReadByte(), 255);
    public virtual DateTime ReadDateTime() => DateTime.FromBinary(ReadInt64());

    public virtual string ReadNetString()
    {
        StringBuilder value = new();
        char character;
        while ((character = ReadChar()) != '\0')
            value.Append(character);
        return value.ToString();
    }
}
