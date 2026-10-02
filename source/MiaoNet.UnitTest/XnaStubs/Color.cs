namespace Microsoft.Xna.Framework;

// Stand-in for XNA's Color so the ui sources (and the chat model) can be compiled into this
// project without Celeste. Same trick as the old Chat/Color stub, but shaped like the real type:
// four channels packed into PackedValue, and the same truncating byte cast in Color * float.
// Only the members actually used are implemented.
public struct Color : IEquatable<Color>
{
    private uint packedValue;

    public Color(byte r, byte g, byte b)
    {
        packedValue = 0u;
        R = r;
        G = g;
        B = b;
        A = byte.MaxValue;
    }

    public Color(byte r, byte g, byte b, byte a)
    {
        packedValue = 0u;
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public byte R
    {
        readonly get => (byte)packedValue;
        set => packedValue = (packedValue & 0xFFFFFF00u) | value;
    }

    public byte G
    {
        readonly get => (byte)(packedValue >> 8);
        set => packedValue = (packedValue & 0xFFFF00FFu) | ((uint)value << 8);
    }

    public byte B
    {
        readonly get => (byte)(packedValue >> 16);
        set => packedValue = (packedValue & 0xFF00FFFFu) | ((uint)value << 16);
    }

    public byte A
    {
        readonly get => (byte)(packedValue >> 24);
        set => packedValue = (packedValue & 0x00FFFFFFu) | ((uint)value << 24);
    }

    public uint PackedValue
    {
        readonly get => packedValue;
        set => packedValue = value;
    }

    public static readonly Color Transparent = new(0, 0, 0, 0);

    public static readonly Color Black = new(0, 0, 0);

    public static readonly Color White = new(255, 255, 255);

    public static readonly Color Gray = new(128, 128, 128);

    public static readonly Color LightGray = new(211, 211, 211);

    public static readonly Color CornflowerBlue = new(100, 149, 237);

    public static readonly Color Cyan = new(0, 255, 255);

    public static readonly Color Yellow = new(255, 255, 0);

    public static readonly Color Wheat = new(245, 222, 179);

    // xna truncates rather than rounds, clamps instead of wrapping, and scales alpha along with
    // rgb. checked against the real FNA assembly: White * 0.5f is 127, White * 2f stays 255.
    public static Color operator *(Color value, float scale)
        => new(
            Clamp(value.R * scale),
            Clamp(value.G * scale),
            Clamp(value.B * scale),
            Clamp(value.A * scale));

    public static Color Lerp(Color value1, Color value2, float amount)
        => new(
            (byte)(value1.R + ((value2.R - value1.R) * amount)),
            (byte)(value1.G + ((value2.G - value1.G) * amount)),
            (byte)(value1.B + ((value2.B - value1.B) * amount)),
            (byte)(value1.A + ((value2.A - value1.A) * amount)));

    public readonly bool Equals(Color other) => packedValue == other.packedValue;

    public readonly override bool Equals(object? obj) => obj is Color other && Equals(other);

    public readonly override int GetHashCode() => packedValue.GetHashCode();

    public readonly override string ToString() => $"{{R:{R} G:{G} B:{B} A:{A}}}";

    public static bool operator ==(Color left, Color right) => left.Equals(right);

    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    private static byte Clamp(float component) => (byte)Math.Clamp(component, 0f, 255f);
}
