using System.Collections.Frozen;

namespace MiaoNet.Shared;

public sealed class PacketDispatcher
{
    private readonly FrozenDictionary<Type, PacketHandler<IContextualPacket>> dictionary;

    private readonly IPacketSerializationContext context;

    public PacketDispatcher(PacketHandlerRegister register, IPacketSerializationContext context)
    {
        dictionary = register.Freeze();
        this.context = context;
    }

    public bool DispatchPacket(PacketEnvelope envelope, IContextualPacket packet)
    {
        if (packet is PacketNetModDelivery delivery)
        {
            MiaoNetNetMods.Dispatch(envelope, delivery, context);
            return true;
        }

        if (!dictionary.TryGetValue(packet.GetType(), out PacketHandler<IContextualPacket>? handler))
            return false;

        handler(envelope, packet);
        return true;
    }
}
