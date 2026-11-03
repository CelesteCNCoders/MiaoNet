using System.Globalization;

namespace MiaoNet.Shared;

public sealed class NetMod : IDisposable
{
    private const int MaxPackets = byte.MaxValue;

    private readonly NetModPacketEntry[] packets;

    private readonly Dictionary<Type, byte> localIDs = new();

    public NetModInfo Info { get; }

    public string Name => Info.Name;

    public NetMod(string name, ushort protocolVersion, params NetModPacketEntry[] packets)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(packets);

        if (packets.Length > MaxPackets)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, SR.NetModPacketIDSpaceExhausted, name, MaxPackets
            ));
        }

        this.packets = (NetModPacketEntry[])packets.Clone();

        for (int i = 0; i < this.packets.Length; i++)
        {
            Type type = this.packets[i].PacketType;
            if (!localIDs.TryAdd(type, (byte)(i + 1)))
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture, SR.NetModPacketTypeAlreadyRegistered, name, type.FullName
                ));
            }
        }

        Info = new(name, protocolVersion);
    }

    public void Send(IContextualPacket packet)
    {
        (byte localID, PacketScope scope) = Resolve(packet);
        if (!scope.IsBroadcast())
            throw ScopeMismatch(packet, scope);

        MiaoNetNetMods.Send(this, localID, PacketRoute.Broadcast(scope), packet);
    }

    public void SendTo(int targetPlayerID, IContextualPacket packet)
    {
        (byte localID, PacketScope scope) = Resolve(packet);
        if (scope != PacketScope.Player)
            throw ScopeMismatch(packet, scope);

        MiaoNetNetMods.Send(this, localID, PacketRoute.Unicast(targetPlayerID), packet);
    }

    public void SendTo(IReadOnlyCollection<int> targetPlayerIDs, IContextualPacket packet)
    {
        (byte localID, PacketScope scope) = Resolve(packet);
        if (scope != PacketScope.Players)
            throw ScopeMismatch(packet, scope);

        MiaoNetNetMods.Send(this, localID, PacketRoute.Multicast(targetPlayerIDs), packet);
    }
    public void Dispose()
        => MiaoNetNetMods.Unregister(this);

    internal bool TryDispatch(
        byte localID,
        PacketEnvelope envelope,
        ReadOnlyMemory<byte> payload,
        IPacketSerializationContext context
    )
    {
        if (localID == 0 || localID > packets.Length)
            return false;

        NetModPacketEntry packet = packets[localID - 1];

        RefBinaryReader inner = new(payload.Span);
        IContextualPacket decoded = packet.Reader(ref inner, context);
        if (inner.BytesLeft != 0)
        {
            throw new InvalidDataException(string.Format(
                CultureInfo.InvariantCulture, SR.PacketHasDataLeft, localID, inner.BytesLeft
            ));
        }

        packet.Invoke(envelope, decoded);
        return true;
    }

    private (byte LocalID, PacketScope Scope) Resolve(IContextualPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (!localIDs.TryGetValue(packet.GetType(), out byte localID))
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, SR.NetModPacketNotDeclared, Name, packet.GetType().FullName
            ));
        }

        return (localID, packets[localID - 1].Scope);
    }

    private InvalidOperationException ScopeMismatch(IContextualPacket packet, PacketScope scope)
        => new(string.Format(
            CultureInfo.InvariantCulture, SR.NetModSendNotAllowed, Name, packet.GetType().Name, scope
        ));
}
