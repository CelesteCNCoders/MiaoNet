using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
public class PooledStringManagerTests
{
    // Server unit test environment defines CONCURRENT; no runtime check needed.

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
    public void EndToEnd_PooledString_SerializeDeserialize_RoundTrip()
    {
        // sender and receiver have independent managers (empty initial)
        var sender = new PooledStringManager(Enumerable.Empty<string>());
        var receiver = new PooledStringManager(Enumerable.Empty<string>());

        static byte[] SendOne(PooledString ps, PooledStringManager mgr)
        {
            using var ms = new MemoryStream();
            var w = new RefBinaryWriter(ms);
            w.Write(ps, mgr);
            return ms.ToArray();
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
        using (var ms = new MemoryStream())
        {
            var w = new RefBinaryWriter(ms);
            w.Write(new PooledString(""), sender);
            var r = new RefBinaryReader(ms.ToArray());
            var s = PooledString.Deserialize(ref r, receiver);
            Assert.AreEqual("", (string)s);
        }

        // near 65535 UTF-8 bytes string (exact 65535 bytes)
        int len = 65535; // RefBinaryWriter encodes length as ushort
        string big = new string('A', len);
        Assert.AreEqual(len, Encoding.UTF8.GetByteCount(big));

        using (var ms = new MemoryStream())
        {
            var w = new RefBinaryWriter(ms);
            w.Write(new PooledString(big), sender);
            var r = new RefBinaryReader(ms.ToArray());
            var s = PooledString.Deserialize(ref r, receiver);
            Assert.AreEqual(big, (string)s);
        }
    }

    [TestMethod]
    public void Concurrent_GetOrCreateID_SameValue_OnlyOneNew()
    {
        var m = new PooledStringManager(Enumerable.Empty<string>());
        int n = 64;
        var start = new ManualResetEventSlim(false);
        var results = new (bool existed, int id)[n];
        var tasks = new Task[n];
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            tasks[i] = Task.Run(() =>
            {
                start.Wait(TestContext.CancellationToken);
                bool existed = m.GetOrCreateID("Walk", out int id);
                results[idx] = (existed, id);
            }, TestContext.CancellationToken);
        }
        start.Set();
        Task.WaitAll(tasks, TestContext.CancellationToken);

        // Ensure all ids are the same
        int theId = results[0].id;
        Assert.IsTrue(results.All(r => r.id == theId));
        // Exactly one should report newly created (existed == false)
        int news = results.Count(r => r.existed == false);
        Assert.AreEqual(1, news);
    }

    [TestMethod]
    public void Concurrent_GetOrCreateID_DistinctValues_AllUnique()
    {
        var m = new PooledStringManager(Enumerable.Empty<string>());
        int n = 500;
        var start = new ManualResetEventSlim(false);
        var ids = new ConcurrentBag<int>();
        var tasks = new Task[n];
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            tasks[i] = Task.Run(() =>
            {
                start.Wait(TestContext.CancellationToken);
                bool existed = m.GetOrCreateID($"S{idx}", out int id);
                Assert.IsFalse(existed);
                ids.Add(id);
            }, TestContext.CancellationToken);
        }
        start.Set();
        Task.WaitAll(tasks, TestContext.CancellationToken);

        var arr = ids.ToArray();
        Assert.AreEqual(n, arr.Distinct().Count());
        // Monotonic not guaranteed under concurrency, but range should be 1..n
        Assert.IsGreaterThanOrEqualTo(1, arr.Min());
        Assert.IsLessThanOrEqualTo(n, arr.Max());
    }

    [TestMethod]
    public void Concurrent_GetAndRecord_SameId_SameValue_AllOk()
    {
        var m = new PooledStringManager(Enumerable.Empty<string>());
        int n = 64;
        var start = new ManualResetEventSlim(false);
        var outputs = new ConcurrentBag<string>();
        var tasks = new Task[n];
        for (int i = 0; i < n; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                start.Wait(TestContext.CancellationToken);
                string s = m.GetAndRecord(42, "Jump");
                outputs.Add(s);
            }, TestContext.CancellationToken);
        }
        start.Set();
        Task.WaitAll(tasks, TestContext.CancellationToken);

        Assert.HasCount(n, outputs);
        Assert.IsTrue(outputs.All(s => s == "Jump"));
    }

    [TestMethod]
    public void Concurrent_GetAndRecord_SameId_DifferentValue_OneWins()
    {
        var m = new PooledStringManager(Enumerable.Empty<string>());
        var start = new ManualResetEventSlim(false);

        Exception? e1 = null, e2 = null;
        string? r1 = null, r2 = null;

        var t1 = Task.Run(() =>
        {
            start.Wait(TestContext.CancellationToken);
            try { r1 = m.GetAndRecord(7, "A"); } catch (Exception ex) { e1 = ex; }
        }, TestContext.CancellationToken);
        var t2 = Task.Run(() =>
        {
            start.Wait(TestContext.CancellationToken);
            try { r2 = m.GetAndRecord(7, "B"); } catch (Exception ex) { e2 = ex; }
        }, TestContext.CancellationToken);

        start.Set();
        Task.WaitAll(t1, t2);

        // Exactly one should succeed and the other must throw mismatch
        int success = (r1 == "A" ? 1 : 0) + (r2 == "B" ? 1 : 0);
        int failures = (e1 != null ? 1 : 0) + (e2 != null ? 1 : 0);
        Assert.AreEqual(1, success);
        Assert.AreEqual(1, failures);

        // The winner's value becomes the mapping
        string mapped = m.GetAndRecord(7, null);
        Assert.IsTrue(mapped is "A" or "B");
    }

    [TestMethod]
    public void EndToEnd_ManyValues_ParallelDeserialize_SafeWhenConcurrent()
    {
        var sender = new PooledStringManager(Enumerable.Empty<string>());
        var receiver = new PooledStringManager(Enumerable.Empty<string>());

        // prepare payloads serially from sender (single-threaded send simulation)
        var values = Enumerable.Range(0, 200).Select(i => new PooledString($"Str_{i:D4}")).ToArray();
        var payloads = new byte[values.Length][];
        for (int i = 0; i < values.Length; i++)
        {
            using var ms = new MemoryStream();
            var w = new RefBinaryWriter(ms);
            w.Write(values[i], sender);
            payloads[i] = ms.ToArray();
        }

        Parallel.For(0, payloads.Length, i =>
        {
            var r = new RefBinaryReader(payloads[i]);
            var s = PooledString.Deserialize(ref r, receiver);
            Assert.AreEqual(values[i].Value, (string)s);
        });
    }

    public TestContext TestContext { get; set; }
}
