using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;

namespace MiaoNet.Shared;

/// <summary>
/// A scene owns its connection-independent encoding. Envelopes, request IDs and
/// transfer IDs are deliberately not cached here. No pooled buffer outlives a send.
/// </summary>
internal sealed class WatchSceneEncoding
{
    private static readonly Meter meter = new("MiaoNet.WatchEncoding");
    private static readonly Counter<long> encodedBodies = meter.CreateCounter<long>("watch.scene.encoded_bodies");
    private static readonly Counter<long> encodedBytes = meter.CreateCounter<long>("watch.scene.encoded_bytes", "By");
    private static readonly Histogram<double> encodingTime = meter.CreateHistogram<double>("watch.scene.encoding_time", "ms");

    private ReadOnlyMemory<byte> payload;
    private bool initialized;

    internal static ReadOnlyMemory<byte> Get<T>(ref WatchSceneEncoding? cache, T scene)
        where T : IRefBinarySerializable<T>
        => LazyInitializer.EnsureInitialized(ref cache, static () => new()).GetPayload(scene);

    private ReadOnlyMemory<byte> GetPayload<T>(T scene) where T : IRefBinarySerializable<T>
    {
        if (!Volatile.Read(ref initialized))
        {
            lock (this)
            {
                if (!initialized)
                {
                    long started = encodingTime.Enabled ? Stopwatch.GetTimestamp() : 0;
                    // Exact sizing avoids MemoryStream growth/copying, especially for small deltas.
                    byte[] bytes = new byte[Measure(scene)];
                    using MemoryStream stream = new(bytes, writable: true);
                    RefBinaryWriter writer = new(stream);
                    writer.Write(scene);
                    if (stream.Position != bytes.Length)
                        throw new InvalidDataException("Watch scene encoding length mismatch.");
                    payload = bytes;
                    Volatile.Write(ref initialized, true);
                    encodedBodies.Add(1);
                    encodedBytes.Add(bytes.Length);
                    if (started != 0)
                        encodingTime.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
            }
        }
        return payload;
    }

    private static int Measure<T>(T scene) => checked(scene switch
    {
        WatchSceneSnapshot snapshot => LocationSize(snapshot.Location) + 12
            + FlagsSize(snapshot.Flags) + StatesSize(snapshot.EntityStates),
        WatchSceneDelta delta => 12 + LocationSize(delta.Location)
            + FlagsSize(delta.AddedFlags) + FlagsSize(delta.RemovedFlags) + 4
            + (delta.RoomTransition is { } transition
                ? LocationSize(transition.SourceLocation) + LocationSize(transition.TargetLocation) + 16 : 0)
            + StatesSize(delta.EntityStates) + EventsSize(delta.EntityEvents),
        _ => throw new ArgumentException("Only Watch scene bodies can share an encoding.", nameof(scene)),
    });

    private static int LocationSize(PlayerLocation location)
        => checked(2 + Encoding.UTF8.GetByteCount(location.Map.Sid)
            + (location.Map.IsEmpty ? 0 : 3 + Encoding.UTF8.GetByteCount(location.Room)));

    private static int FlagsSize(IReadOnlyCollection<string> flags)
    {
        int size = 2;
        foreach (string flag in flags)
            size = checked(size + 2 + Encoding.UTF8.GetByteCount(flag));
        return size;
    }

    private static int StatesSize(IReadOnlyCollection<WatchEntityState> states)
    {
        int size = 2;
        foreach (WatchEntityState state in states)
            size = checked(size + 10 + state.Payload.Length);
        return size;
    }

    private static int EventsSize(IReadOnlyCollection<WatchEntityEvent> events)
    {
        int size = 2;
        foreach (WatchEntityEvent entityEvent in events)
            size = checked(size + 11 + entityEvent.Payload.Length);
        return size;
    }

    // Freeze collection membership so validation, cached bytes and local replay
    // cannot disagree if a caller later edits its input collection.
    internal static IReadOnlyCollection<T> Freeze<T>(IReadOnlyCollection<T> values)
        => values.Count == 0 ? Array.Empty<T>() : Array.AsReadOnly(values.ToArray());
}
