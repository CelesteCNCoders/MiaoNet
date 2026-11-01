using System.Text;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet;

public class CelesteNetBinaryWriter : BinaryWriter
{
    public DataContext Data { get; }

    public CelesteNetBinaryWriter(DataContext data, Stream output, bool leaveOpen = false)
        : base(output, Encoding.UTF8, leaveOpen)
    {
        Data = data;
    }

    public void Write7BitEncodedUInt(uint value) => Write7BitEncodedInt(unchecked((int)value));
    public virtual void Write(Vector2 value) { Write(value.X); Write(value.Y); }
    public virtual void Write(Color value) { Write(value.R); Write(value.G); Write(value.B); Write(value.A); }
    public virtual void WriteNoA(Color value) { Write(value.R); Write(value.G); Write(value.B); }
    public virtual void Write(DateTime value) => Write(value.ToBinary());

    public virtual void WriteNetString(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            Write(value.ToCharArray());
        Write('\0');
    }
}
