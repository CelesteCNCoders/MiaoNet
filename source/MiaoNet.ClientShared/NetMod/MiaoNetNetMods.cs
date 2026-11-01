using System.Globalization;

namespace MiaoNet.Shared;

public static class MiaoNetNetMods
{
    private static readonly List<NetMod> mods = [];

    private static readonly Dictionary<NetMod, byte> indices = [];

    private static INetModTransport? transport;

    internal static event Action? Changed;

    public static bool IsConnected => transport is not null;

    public static NetMod Register(NetMod netMod)
    {
        ArgumentNullException.ThrowIfNull(netMod);

        if (mods.Any(m => string.Equals(m.Name, netMod.Name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, SR.NetModAlreadyRegistered, netMod.Name
            ));
        }

        if (mods.Count >= NetModInfo.MaxNetMods)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, SR.NetModIDSpaceExhausted, NetModInfo.MaxNetMods
            ));
        }

        indices.Add(netMod, (byte)mods.Count);
        mods.Add(netMod);

        Changed?.Invoke();
        return netMod;
    }

    internal static void SetTransport(INetModTransport? value)
        => transport = value;

    internal static NetModInfo[] BuildHandshakeNetMods()
    {
        NetModInfo[] result = new NetModInfo[mods.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = mods[i].Info;
        return result;
    }

    internal static void Send(NetMod netMod, byte localID, PacketRoute route, IContextualPacket packet)
    {
        if (!indices.TryGetValue(netMod, out byte index))
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, SR.NetModUnregistered, netMod.Name
            ));
        }

        INetModTransport current = transport
            ?? throw new InvalidOperationException(SR.NetModNotConnected);

        current.Send(index, localID, route, packet);
    }

    internal static void Unregister(NetMod netMod)
    {
        if (!indices.Remove(netMod))
            return;

        mods.Remove(netMod);

        for (int i = 0; i < mods.Count; i++)
            indices[mods[i]] = (byte)i;

        Changed?.Invoke();
    }

    internal static void Dispatch(
        PacketEnvelope envelope,
        PacketNetModDelivery delivery,
        IPacketSerializationContext context
    )
    {
        if (!envelope.HasSender)
            throw new InvalidDataException(SR.NetModPacketHasNoSender);

        if (delivery.NetModID >= mods.Count || !mods[delivery.NetModID].TryDispatch(delivery.LocalID, envelope, delivery.Payload, context))
        {
            throw new InvalidDataException(string.Format(
                CultureInfo.InvariantCulture, SR.UnknownNetModPacket, delivery.NetModID, delivery.LocalID
            ));
        }
    }
}
