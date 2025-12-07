namespace MiaoNet.Server.Primitives;

public struct Color
{
    private uint packedValue;

    public byte R
    {
        readonly get => (byte)packedValue;
        set => packedValue = (packedValue & 0xFFFFFF00u) | value;
    }

    public byte G
    {
        readonly get => (byte)(packedValue >> 8);
        set => packedValue = (packedValue & 0xFFFF00FFu) | (uint)(value << 8);
    }

    public byte B
    {
        readonly get => (byte)(packedValue >> 16);
        set => packedValue = (packedValue & 0xFF00FFFFu) | (uint)(value << 16);
    }

    public byte A
    {
        readonly get => (byte)(packedValue >> 24);
        set => packedValue = (packedValue & 0x00FFFFFFu) | (uint)(value << 24);
    }

    public uint PackedValue
    {
        readonly get => packedValue;
        set => packedValue = value;
    }

    public Color(int r, int g, int b)
    {
        packedValue = 0u;
        R = (byte)Math.Clamp(r, 0, 255);
        G = (byte)Math.Clamp(g, 0, 255);
        B = (byte)Math.Clamp(b, 0, 255);
        A = byte.MaxValue;
    }

    public Color(int r, int g, int b, int alpha)
    {
        packedValue = 0u;
        R = (byte)Math.Clamp(r, 0, 255);
        G = (byte)Math.Clamp(g, 0, 255);
        B = (byte)Math.Clamp(b, 0, 255);
        A = (byte)Math.Clamp(alpha, 0, 255);
    }
}