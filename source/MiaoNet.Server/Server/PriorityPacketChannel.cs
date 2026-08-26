using System.Threading.Channels;
using MiaoNet.Shared;

namespace MiaoNet.Server;

internal sealed class PriorityPacketChannel
{
    private readonly Channel<IContextualPacket> control;
    private readonly Channel<IContextualPacket> playerFrame;
    private readonly Channel<IContextualPacket> general;
    private readonly Channel<IContextualPacket> watchEntity;
    private readonly Channel<byte> ready = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });

    internal PriorityPacketChannel(
        int controlCapacity,
        int playerFrameCapacity,
        int generalCapacity,
        int watchEntityCapacity
    )
    {
        control = CreateLane(controlCapacity);
        playerFrame = CreateLane(playerFrameCapacity);
        general = CreateLane(generalCapacity);
        watchEntity = CreateLane(watchEntityCapacity);
    }

    internal bool TryWrite(IContextualPacket packet)
    {
        ChannelWriter<IContextualPacket> writer = GetWriter(
            PacketPriorityClassifier.Classify(packet)
        );
        if (!writer.TryWrite(packet))
            return false;

        if (!ready.Writer.TryWrite(0))
            throw new InvalidOperationException("The packet-ready channel was unexpectedly closed.");
        return true;
    }

    internal async ValueTask WriteAsync(IContextualPacket packet, CancellationToken token)
    {
        await GetWriter(PacketPriorityClassifier.Classify(packet)).WriteAsync(packet, token);
        if (!ready.Writer.TryWrite(0))
            throw new ChannelClosedException();
    }

    internal bool TryRead(out IContextualPacket packet)
    {
        while (ready.Reader.TryRead(out _))
        {
            if (TryReadPrioritized(out packet))
                return true;
        }

        packet = null!;
        return false;
    }

    internal async ValueTask<bool> WaitToReadAsync(CancellationToken token)
        => await ready.Reader.WaitToReadAsync(token);

    internal void Complete()
    {
        control.Writer.TryComplete();
        playerFrame.Writer.TryComplete();
        general.Writer.TryComplete();
        watchEntity.Writer.TryComplete();
        ready.Writer.TryComplete();
    }

    private bool TryReadPrioritized(out IContextualPacket packet)
        => control.Reader.TryRead(out packet!)
            || playerFrame.Reader.TryRead(out packet!)
            || general.Reader.TryRead(out packet!)
            || watchEntity.Reader.TryRead(out packet!);

    private ChannelWriter<IContextualPacket> GetWriter(PacketPriority priority)
        => priority switch
        {
            PacketPriority.Control => control.Writer,
            PacketPriority.PlayerFrame => playerFrame.Writer,
            PacketPriority.General => general.Writer,
            PacketPriority.WatchEntity => watchEntity.Writer,
            _ => throw new ArgumentOutOfRangeException(nameof(priority)),
        };

    private static Channel<IContextualPacket> CreateLane(int capacity)
        => Channel.CreateBounded<IContextualPacket>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
}
