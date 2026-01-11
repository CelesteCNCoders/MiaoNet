// write by qwen, they're good at doing these
#pragma warning disable IDE0251

using MiaoNet.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace MiaoNet.UnitTest;

[TestClass]
public class RefBinaryReaderWriterTests
{
    [TestMethod]
    public void TestBasicTypes()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        // Write basic types
        writer.Write(true);
        writer.Write((byte)255);
        writer.Write((short)-32768);
        writer.Write((ushort)65535);
        writer.Write(-2147483648);
        writer.Write(4294967295U);
        writer.Write(-9223372036854775808L);
        writer.Write(18446744073709551615UL);
        writer.Write(3.14159f);
        writer.Write(3.141592653589793);
        writer.Write((Half)2.5f);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        // Read basic types
        Assert.IsTrue(reader.ReadBoolean());
        Assert.AreEqual((byte)255, reader.ReadByte());
        Assert.AreEqual((short)-32768, reader.ReadInt16());
        Assert.AreEqual((ushort)65535, reader.ReadUInt16());
        Assert.AreEqual(-2147483648, reader.ReadInt32());
        Assert.AreEqual(4294967295U, reader.ReadUInt32());
        Assert.AreEqual(-9223372036854775808L, reader.ReadInt64());
        Assert.AreEqual(18446744073709551615UL, reader.ReadUInt64());
        Assert.AreEqual(3.14159f, reader.ReadSingle(), 0.00001f);
        Assert.AreEqual(3.141592653589793, reader.ReadDouble(), 0.000000000000001);
        Assert.AreEqual((Half)2.5f, reader.ReadHalf());
    }

    [TestMethod]
    public void Test7BitEncodedInt()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        // Write 7-bit encoded integers
        writer.Write7BitEncodedInt(0);
        writer.Write7BitEncodedInt(127);
        writer.Write7BitEncodedInt(128);
        writer.Write7BitEncodedInt(16383);
        writer.Write7BitEncodedInt(16384);
        writer.Write7BitEncodedInt(2097151);
        writer.Write7BitEncodedInt(2097152);
        writer.Write7BitEncodedInt(int.MaxValue);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        // Read 7-bit encoded integers
        Assert.AreEqual(0, reader.Read7BitEncodedInt());
        Assert.AreEqual(127, reader.Read7BitEncodedInt());
        Assert.AreEqual(128, reader.Read7BitEncodedInt());
        Assert.AreEqual(16383, reader.Read7BitEncodedInt());
        Assert.AreEqual(16384, reader.Read7BitEncodedInt());
        Assert.AreEqual(2097151, reader.Read7BitEncodedInt());
        Assert.AreEqual(2097152, reader.Read7BitEncodedInt());
        Assert.AreEqual(int.MaxValue, reader.Read7BitEncodedInt());
    }

    [TestMethod]
    public void Test7BitEncodedInt64()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        // Write 7-bit encoded long integers
        writer.Write7BitEncodedInt64(0);
        writer.Write7BitEncodedInt64(127);
        writer.Write7BitEncodedInt64(128);
        writer.Write7BitEncodedInt64(16383);
        writer.Write7BitEncodedInt64(16384);
        writer.Write7BitEncodedInt64(2097151);
        writer.Write7BitEncodedInt64(2097152);
        writer.Write7BitEncodedInt64(long.MaxValue);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        // Read 7-bit encoded long integers
        Assert.AreEqual(0, reader.Read7BitEncodedInt64());
        Assert.AreEqual(127, reader.Read7BitEncodedInt64());
        Assert.AreEqual(128, reader.Read7BitEncodedInt64());
        Assert.AreEqual(16383, reader.Read7BitEncodedInt64());
        Assert.AreEqual(16384, reader.Read7BitEncodedInt64());
        Assert.AreEqual(2097151, reader.Read7BitEncodedInt64());
        Assert.AreEqual(2097152, reader.Read7BitEncodedInt64());
        Assert.AreEqual(long.MaxValue, reader.Read7BitEncodedInt64());
    }

    [TestMethod]
    public void TestSpanOperations()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        writer.WriteSpan(data);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        var readSpan = reader.ReadSpan(8);
        CollectionAssert.AreEqual(data, readSpan.ToArray());
    }

    [TestMethod]
    public void TestVersion()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        var version = new Version(1, 2, 3);
        writer.Write(version);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        var readVersion = reader.ReadVersion();
        Assert.AreEqual(version.Major, readVersion.Major);
        Assert.AreEqual(version.Minor, readVersion.Minor);
        Assert.AreEqual(version.Build, readVersion.Build);
    }

    [TestMethod]
    public void TestString()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        string testString = "Hello, 世界! 🌍";
        writer.Write(testString);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        var readString = reader.ReadString();
        Assert.AreEqual(testString, readString);
    }

    [TestMethod]
    public void TestStringWithCustomEncoding()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        string testString = "Hello, 世界!";
        Encoding encoding = Encoding.Unicode;
        writer.Write(testString, encoding);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        var readString = reader.ReadString(encoding);
        Assert.AreEqual(testString, readString);
    }

    [TestMethod]
    public void TestColor()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        var color = new Color();
        color = new Color(255, 128, 64, 32);
        writer.Write(color);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        var readColor = reader.ReadColor();
        Assert.AreEqual(color.R, readColor.R);
        Assert.AreEqual(color.G, readColor.G);
        Assert.AreEqual(color.B, readColor.B);
        Assert.AreEqual(color.A, readColor.A);
    }

    [TestMethod]
    public void TestSerializableType()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        var person = new Person { Name = "John", Age = 30 };
        writer.Write(person);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        var readPerson = reader.Read<Person>();
        Assert.AreEqual(person.Name, readPerson.Name);
        Assert.AreEqual(person.Age, readPerson.Age);
    }

    [TestMethod]
    public void TestList()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        var people = new List<Person>
        {
            new Person { Name = "Alice", Age = 25 },
            new Person { Name = "Bob", Age = 35 },
            new Person { Name = "Charlie", Age = 45 }
        };
        writer.Write(people);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        var readPeople = reader.ReadArray<Person>();
        Assert.HasCount(people.Count, readPeople);
        for (int i = 0; i < people.Count; i++)
        {
            Assert.AreEqual(people[i].Name, readPeople[i].Name);
            Assert.AreEqual(people[i].Age, readPeople[i].Age);
        }
    }

    [TestMethod]
    public void TestEdgeCases()
    {
        // Should throw when trying to read from empty span
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        {
            // Test with empty span
            new RefBinaryReader(Array.Empty<byte>()).ReadByte();
        });
    }

    [TestMethod]
    public void TestLargeString()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        string largeString = new string('A', 10000);
        writer.Write(largeString);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        var readString = reader.ReadString();
        Assert.AreEqual(largeString, readString);
    }

    [TestMethod]
    public void TestMaxValues()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        writer.Write(byte.MaxValue);
        writer.Write(short.MaxValue);
        writer.Write(ushort.MaxValue);
        writer.Write(int.MaxValue);
        writer.Write(uint.MaxValue);
        writer.Write(long.MaxValue);
        writer.Write(ulong.MaxValue);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        Assert.AreEqual(byte.MaxValue, reader.ReadByte());
        Assert.AreEqual(short.MaxValue, reader.ReadInt16());
        Assert.AreEqual(ushort.MaxValue, reader.ReadUInt16());
        Assert.AreEqual(int.MaxValue, reader.ReadInt32());
        Assert.AreEqual(uint.MaxValue, reader.ReadUInt32());
        Assert.AreEqual(long.MaxValue, reader.ReadInt64());
        Assert.AreEqual(ulong.MaxValue, reader.ReadUInt64());
    }

    [TestMethod]
    public void TestMinValues()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        writer.Write(short.MinValue);
        writer.Write(int.MinValue);
        writer.Write(long.MinValue);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        Assert.AreEqual(short.MinValue, reader.ReadInt16());
        Assert.AreEqual(int.MinValue, reader.ReadInt32());
        Assert.AreEqual(long.MinValue, reader.ReadInt64());
    }

    [TestMethod]
    public void TestSpecialFloatValues()
    {
        using var ms = new MemoryStream();
        var writer = new RefBinaryWriter(ms);

        writer.Write(float.NaN);
        writer.Write(float.PositiveInfinity);
        writer.Write(float.NegativeInfinity);
        writer.Write(double.NaN);
        writer.Write(double.PositiveInfinity);
        writer.Write(double.NegativeInfinity);

        ms.Position = 0;
        var reader = new RefBinaryReader(ms.ToArray());

        Assert.IsTrue(float.IsNaN(reader.ReadSingle()));
        Assert.IsTrue(float.IsPositiveInfinity(reader.ReadSingle()));
        Assert.IsTrue(float.IsNegativeInfinity(reader.ReadSingle()));
        Assert.IsTrue(double.IsNaN(reader.ReadDouble()));
        Assert.IsTrue(double.IsPositiveInfinity(reader.ReadDouble()));
        Assert.IsTrue(double.IsNegativeInfinity(reader.ReadDouble()));
    }
}

public struct Person : IRefBinarySerializable<Person>
{
    public string Name { get; set; }
    public int Age { get; set; }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Name);
        writer.Write(Age);
    }

    public static Person Deserialize(ref RefBinaryReader reader)
    {
        return new Person
        {
            Name = reader.ReadString(),
            Age = reader.ReadInt32()
        };
    }
}

public struct Point : IRefBinarySerializable<Point>
{
    public int X { get; set; }
    public int Y { get; set; }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
    }

    public static Point Deserialize(ref RefBinaryReader reader)
    {
        return new Point
        {
            X = reader.ReadInt32(),
            Y = reader.ReadInt32()
        };
    }
}

public struct Rectangle : IRefBinarySerializable<Rectangle>
{
    public Point TopLeft { get; set; }
    public Point BottomRight { get; set; }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(TopLeft);
        writer.Write(BottomRight);
    }

    public static Rectangle Deserialize(ref RefBinaryReader reader)
    {
        return new Rectangle
        {
            TopLeft = reader.Read<Point>(),
            BottomRight = reader.Read<Point>()
        };
    }
}
