namespace Microsoft.Xna.Framework;

// Stand-in for XNA's Matrix, see Color.cs. Only the 2d part the scissor math reads
// (M11/M12/M21/M22/M41/M42) plus the factories the tests build transforms with.
public struct Matrix : IEquatable<Matrix>
{
    public float M11, M12, M13, M14;
    public float M21, M22, M23, M24;
    public float M31, M32, M33, M34;
    public float M41, M42, M43, M44;

    public static readonly Matrix Identity = new()
    {
        M11 = 1f,
        M22 = 1f,
        M33 = 1f,
        M44 = 1f
    };

    public static Matrix CreateScale(float scale)
        => new()
        {
            M11 = scale,
            M22 = scale,
            M33 = scale,
            M44 = 1f
        };

    public static Matrix CreateScale(float xScale, float yScale, float zScale)
        => new()
        {
            M11 = xScale,
            M22 = yScale,
            M33 = zScale,
            M44 = 1f
        };

    public static Matrix CreateTranslation(float xPosition, float yPosition, float zPosition)
        => new()
        {
            M11 = 1f,
            M22 = 1f,
            M33 = 1f,
            M44 = 1f,
            M41 = xPosition,
            M42 = yPosition,
            M43 = zPosition
        };

    public readonly bool Equals(Matrix other)
        => M11 == other.M11 && M12 == other.M12 && M13 == other.M13 && M14 == other.M14
        && M21 == other.M21 && M22 == other.M22 && M23 == other.M23 && M24 == other.M24
        && M31 == other.M31 && M32 == other.M32 && M33 == other.M33 && M34 == other.M34
        && M41 == other.M41 && M42 == other.M42 && M43 == other.M43 && M44 == other.M44;

    public readonly override bool Equals(object? obj) => obj is Matrix other && Equals(other);

    public readonly override int GetHashCode() => HashCode.Combine(M11, M12, M21, M22, M41, M42);

    public static bool operator ==(Matrix left, Matrix right) => left.Equals(right);

    public static bool operator !=(Matrix left, Matrix right) => !left.Equals(right);
}
