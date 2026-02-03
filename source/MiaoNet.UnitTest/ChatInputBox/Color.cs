namespace Celeste.Mod.ChatInputBox;

public readonly struct Color : IEquatable<Color>
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    public Color(byte r, byte g, byte b) => (R, G, B) = (r, g, b);
    public Color(byte r, byte g, byte b, byte _) => (R, G, B) = (r, g, b);

    public static Color FromArgb(byte r, byte g, byte b) => new(r, g, b);

    public bool Equals(Color other) => R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is Color c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(R, G, B);
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
    public static bool operator ==(Color left, Color right) => left.Equals(right);
    public static bool operator !=(Color left, Color right) => !(left == right);
}