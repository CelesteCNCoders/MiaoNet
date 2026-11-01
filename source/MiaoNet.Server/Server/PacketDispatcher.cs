using System.Collections.Frozen;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class PacketDispatcher
{
    private readonly FrozenDictionary<Type, PacketHandler<IContextualPacket>> dictionary;

    public PacketDispatcher(PacketHandlerRegister register)
        => dictionary = register.Freeze();

    public async ValueTask<bool> DispatchPacketAsync(
        MiaoClientConnection connection,
        PacketEnvelope envelope,
        IContextualPacket packet
    )
    {
        if (!dictionary.TryGetValue(packet.GetType(), out PacketHandler<IContextualPacket>? handler))
            return false;

        await handler(connection, envelope, packet);
        return true;
    }
}
