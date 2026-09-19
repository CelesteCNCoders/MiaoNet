using System.Globalization;
using System.Text;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public class PooledStringManagerTests
{
    [TestMethod]
    public void GetOrCreateID_BasicSequence_NoInitial()
    {
        var m = new PooledStringManager(Enumerable.Empty<string>());

        Assert.IsFalse(m.GetOrCreateID("Walk", out int id1));
        Assert.AreEqual(1, id1);

        Assert.IsTrue(m.GetOrCreateID("Walk", out int id1Again));
        Assert.AreEqual(id1, id1Again);

        Assert.IsFalse(m.GetOrCreateID("Jump", out int id2));
        Assert.AreEqual(2, id2);
    }

    [TestMethod]
    public void GetAndRecord_BasicAdd_ThenReadWithoutValue()
    {
        var m = new PooledStringManager(Enumerable.Empty<string>());

        // First time: must come with value to establish mapping
        string v = m.GetAndRecord(1, "Walk");
        Assert.AreEqual("Walk", v);

        // Next time: no value required
        string v2 = m.GetAndRecord(1, null);
        Assert.AreEqual("Walk", v2);
    }

    [TestMethod]
    public void GetAndRecord_MissingValue_Throws()
    {
        var m = new PooledStringManager(Enumerable.Empty<string>());
        var ex = Assert.Throws<InvalidDataException>(() => m.GetAndRecord(1, null));
        Assert.Contains("missing", ex.Message);
    }

    [TestMethod]
    public void GetAndRecord_ValueMismatch_Throws()
    {
        var m = new PooledStringManager(Enumerable.Empty<string>());
        Assert.AreEqual("Walk", m.GetAndRecord(1, "Walk"));
        var ex = Assert.Throws<InvalidDataException>(() => m.GetAndRecord(1, "Jump"));
        Assert.Contains("different value", ex.Message);
        // original mapping intact
        Assert.AreEqual("Walk", m.GetAndRecord(1, null));
    }

    [TestMethod]
    public void InitialStrings_KeepOneBasedIDsAndSetNextIDForBothDirections()
    {
        string[] initial = ["KnownA", "KnownB"];
        var m = new PooledStringManager(initial);

        Assert.IsTrue(m.GetOrCreateID("KnownA", out int knownLocalID));
        Assert.AreEqual(1, knownLocalID);
        Assert.IsFalse(m.GetOrCreateID("NewLocal", out int newLocalID));
        Assert.AreEqual(3, newLocalID);

        Assert.AreEqual("KnownA", m.GetAndRecord(1, null));
        Assert.AreEqual("KnownB", m.GetAndRecord(2, "KnownB"));
        Assert.AreEqual("NewRemote", m.GetAndRecord(3, "NewRemote"));
    }

    [TestMethod]
    public void InitialStrings_AreEnumeratedOnceToKeepBothDirectionsConsistent()
    {
        int enumerationCount = 0;
        IEnumerable<string> GetInitialStrings()
        {
            enumerationCount++;
            yield return "Known";
        }

        var m = new PooledStringManager(GetInitialStrings());

        Assert.AreEqual(1, enumerationCount);
        Assert.IsTrue(m.GetOrCreateID("Known", out int id));
        Assert.AreEqual(1, id);
        Assert.AreEqual("Known", m.GetAndRecord(1, null));
    }

    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    [TestMethod]
    public void GetAndRecord_RejectsNonPositiveID(int id)
    {
        var m = new PooledStringManager([]);

        Assert.Throws<InvalidDataException>(() => m.GetAndRecord(id, "invalid"));
    }

    [TestMethod]
    public void GetAndRecord_RejectsSparseNewIDsWithoutAdvancingSequence()
    {
        var m = new PooledStringManager([]);

        Assert.Throws<InvalidDataException>(() => m.GetAndRecord(2, "sparse"));
        Assert.AreEqual("first", m.GetAndRecord(1, "first"));
        Assert.Throws<InvalidDataException>(() => m.GetAndRecord(3, "still-sparse"));
        Assert.AreEqual("second", m.GetAndRecord(2, "second"));
    }

    [TestMethod]
    public void GetAndRecord_RepeatedKnownAndLearnedIDsRemainCompatible()
    {
        var m = new PooledStringManager(["Known"]);

        Assert.AreEqual("Known", m.GetAndRecord(1, null));
        Assert.AreEqual("Known", m.GetAndRecord(1, "Known"));
        Assert.AreEqual("Dynamic", m.GetAndRecord(2, "Dynamic"));
        Assert.AreEqual("Dynamic", m.GetAndRecord(2, null));
        Assert.AreEqual("Dynamic", m.GetAndRecord(2, "Dynamic"));
    }

    [TestMethod]
    public void EndToEnd_PooledString_SerializeDeserialize_RoundTrip()
    {
        // sender and receiver have independent managers (empty initial)
        var sender = new PooledStringManager(Enumerable.Empty<string>());
        var receiver = new PooledStringManager(Enumerable.Empty<string>());

        static byte[] SendOne(PooledString ps, PooledStringManager mgr)
        {
            ByteArrayBufferWriter buffer = new();
            var w = new RefBinaryWriter(buffer);
            w.Write(ps, mgr);
            w.Flush();
            return buffer.WrittenSpan.ToArray();
        }

        static string ReceiveOne(byte[] payload, PooledStringManager mgr)
        {
            var r = new RefBinaryReader(payload);
            return PooledString.Deserialize(ref r, mgr);
        }

        // first time should carry value; second time should not
        var bytes1 = SendOne(new PooledString("Jump"), sender);
        var got1 = ReceiveOne(bytes1, receiver);
        Assert.AreEqual("Jump", got1);

        var bytes2 = SendOne(new PooledString("Jump"), sender);
        var got2 = ReceiveOne(bytes2, receiver);
        Assert.AreEqual("Jump", got2);
        Assert.HasCount(1, bytes2);

        // different value obtains a different id on sender, and receiver learns it
        var bytes3 = SendOne(new PooledString("Run"), sender);
        var got3 = ReceiveOne(bytes3, receiver);
        Assert.AreEqual("Run", got3);
    }

    [TestMethod]
    public void Edge_StringEmpty_And_Utf8Boundary()
    {
        var sender = new PooledStringManager(Enumerable.Empty<string>());
        var receiver = new PooledStringManager(Enumerable.Empty<string>());

        // empty string
        {
            ByteArrayBufferWriter buffer = new();
            var w = new RefBinaryWriter(buffer);
            w.Write(new PooledString(""), sender);
            w.Flush();
            var r = new RefBinaryReader(buffer.WrittenSpan);
            var s = PooledString.Deserialize(ref r, receiver);
            Assert.AreEqual("", (string)s);
        }

        string boundary = new string('A', PooledStringManager.MaxRemoteStringUtf8Bytes);
        Assert.AreEqual(PooledStringManager.MaxRemoteStringUtf8Bytes, Encoding.UTF8.GetByteCount(boundary));

        {
            ByteArrayBufferWriter buffer = new();
            var w = new RefBinaryWriter(buffer);
            w.Write(new PooledString(boundary), sender);
            w.Flush();
            var r = new RefBinaryReader(buffer.WrittenSpan);
            var s = PooledString.Deserialize(ref r, receiver);
            Assert.AreEqual(boundary, (string)s);
        }
    }

    [TestMethod]
    public void GetAndRecord_RejectsValueAboveUtf8LimitWithoutConsumingID()
    {
        var m = new PooledStringManager([]);
        string tooLarge = new string('猫', PooledStringManager.MaxRemoteStringUtf8Bytes / 3 + 1);
        Assert.IsGreaterThan(
            PooledStringManager.MaxRemoteStringUtf8Bytes,
            Encoding.UTF8.GetByteCount(tooLarge)
        );

        Assert.Throws<InvalidDataException>(() => m.GetAndRecord(1, tooLarge));
        Assert.AreEqual("valid", m.GetAndRecord(1, "valid"));
    }

    [TestMethod]
    public void GetAndRecord_EnforcesRemoteEntryLimit()
    {
        var m = new PooledStringManager([]);
        for (int id = 1; id <= PooledStringManager.MaxRemoteEntries; id++)
            Assert.AreEqual(string.Empty, m.GetAndRecord(id, string.Empty));

        Assert.Throws<InvalidDataException>(() =>
            m.GetAndRecord(PooledStringManager.MaxRemoteEntries + 1, string.Empty)
        );
    }

    [TestMethod]
    public void GetAndRecord_EnforcesTotalUtf8LimitWithoutConsumingID()
    {
        var m = new PooledStringManager([]);
        int entries = PooledStringManager.MaxRemoteTotalUtf8Bytes
            / PooledStringManager.MaxRemoteStringUtf8Bytes;
        for (int i = 0; i < entries; i++)
        {
            string prefix = i.ToString("D4", CultureInfo.InvariantCulture);
            string value = prefix + new string(
                'A',
                PooledStringManager.MaxRemoteStringUtf8Bytes - prefix.Length
            );
            Assert.AreEqual(PooledStringManager.MaxRemoteStringUtf8Bytes, Encoding.UTF8.GetByteCount(value));
            Assert.AreEqual(value, m.GetAndRecord(i + 1, value));
        }

        int nextID = entries + 1;
        Assert.Throws<InvalidDataException>(() => m.GetAndRecord(nextID, "x"));
        Assert.AreEqual(string.Empty, m.GetAndRecord(nextID, string.Empty));
    }

    [TestMethod]
    public void EndToEnd_ManyValues_ResolveRepeatedlyAfterLearning()
    {
        var sender = new PooledStringManager(Enumerable.Empty<string>());
        var receiver = new PooledStringManager(Enumerable.Empty<string>());

        // Learn new IDs in wire order, as the single receive loop does in production.
        var values = Enumerable.Range(0, 200).Select(i => new PooledString($"Str_{i:D4}")).ToArray();
        var payloads = new byte[values.Length][];
        for (int i = 0; i < values.Length; i++)
        {
            ByteArrayBufferWriter buffer = new();
            var w = new RefBinaryWriter(buffer);
            w.Write(values[i], sender);
            w.Flush();
            var r = new RefBinaryReader(buffer.WrittenSpan);
            Assert.AreEqual(values[i].Value, (string)PooledString.Deserialize(ref r, receiver));
        }

        // References to IDs already learned carry no value and resolve to the same string.
        for (int i = 0; i < values.Length; i++)
        {
            ByteArrayBufferWriter buffer = new();
            var w = new RefBinaryWriter(buffer);
            w.Write(values[i], sender);
            w.Flush();
            payloads[i] = buffer.WrittenSpan.ToArray();
        }

        for (int i = 0; i < payloads.Length; i++)
        {
            var r = new RefBinaryReader(payloads[i]);
            var s = PooledString.Deserialize(ref r, receiver);
            Assert.AreEqual(values[i].Value, (string)s);
        }
    }

    public TestContext TestContext { get; set; }
}
