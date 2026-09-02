using System.Diagnostics;
using MiaoNet.Shared;

namespace MiaoNet.UnitTest;

[TestClass]
[DoNotParallelize]
public sealed class WatchEncodingPerformanceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("Performance")]
    public void ReportSceneFanoutEncodingCost()
    {
        if (Environment.GetEnvironmentVariable("MIAONET_RUN_WATCH_BENCHMARKS") != "1")
            Assert.Inconclusive("Opt in with MIAONET_RUN_WATCH_BENCHMARKS=1; this is not a timing assertion.");

        using MemoryStream destination = new(128 * 1024);
        Context context = new();
        foreach ((int count, int payloadSize) in new[] { (1, 64), (16, 480), (16, 512), (32, 512) })
        {
            WatchEntityState[] states = Enumerable.Range(0, count)
                .Select(i => new WatchEntityState(new(WatchEntityKind.CrumblePlatform, i), new byte[payloadSize]))
                .ToArray();
            foreach (int recipients in new[] { 1, 5, 10 })
            {
                Run(100, recipients, states, destination, context);
                List<double> elapsed = [];
                List<long> allocations = [];
                long bytes = 0;
                const int Iterations = 200;
                for (int sample = 0; sample < 5; sample++)
                {
                    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    bytes = Run(Iterations, recipients, states, destination, context);
                    elapsed.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds / Iterations);
                    allocations.Add((GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / Iterations);
                }
                elapsed.Sort();
                allocations.Sort();
                TestContext.WriteLine(
                    $"WATCH_BENCH states={count} payload={payloadSize} watchers={recipients} " +
                    $"us/scene={elapsed[2]:F2} allocated/scene={allocations[2]} wire/scene={bytes / Iterations}");
            }
        }
    }

    private static long Run(
        int iterations, int recipients, WatchEntityState[] states,
        MemoryStream destination, Context context)
    {
        long bytes = 0;
        for (int i = 0; i < iterations; i++)
        {
            WatchSceneDelta delta = new(i + 1,
                new("Celeste/1-ForsakenCity", AreaMode.Normal, "1"), [], [], false,
                WatchEntityStateMode.Patch, states, [], playerEpoch: 1, playerSequenceWatermark: 2);
            for (int watcher = 0; watcher < recipients; watcher++)
            {
                PacketWatchSceneDeltaNotification packet = new(watcher + 1, 100, delta);
                destination.Position = 0;
                if (WatchSceneFragmenter.TryFragment(packet, out var fragments))
                {
                    foreach (IContextualPacket fragment in fragments)
                        PacketFraming.WritePacket(destination, fragment, context);
                }
                else
                    PacketFraming.WritePacket(destination, packet, context);
                bytes += destination.Position;
            }
        }
        return bytes;
    }

    private sealed class Context : IPacketSerializationContext
    {
        public PooledStringManager PooledStringManager { get; } = new(KnownPooledStrings.All);
    }
}
