using System.Globalization;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed partial class MiaoServerService
{
    internal async ValueTask ForwardNetModPacketAsync(
        MiaoClientConnection connection,
        PacketEnvelope envelope,
        PacketNetMod netMod
    )
    {
        byte netModID = netMod.NetModID;
        if (netModID >= connection.NetMods.Count)
        {
            throw new InvalidDataException(string.Format(
                CultureInfo.InvariantCulture, SR.NetModIDOutOfRange, connection.NetMods.Count, netModID
            ));
        }

        NetModInfo senderMod = connection.NetMods[netModID];
        PacketEnvelope forwarded = PacketEnvelope.FromSender(connection.ID);

        switch (netMod.Route.Scope)
        {
        case PacketScope.Global:
            await ForwardToAsync(connection, netMod, forwarded, PlayersOf(serverState), senderMod, excludeSender: true);
            break;

        case PacketScope.Channel:
            await ForwardToAsync(connection, netMod, forwarded, PlayersOf(connection.Player.Channel), senderMod, excludeSender: true);
            break;

        case PacketScope.Map:
        {
            ServerChannel channel = connection.Player.Channel;
            if (!channel.Maps.TryGetValue(connection.Player.Location.Map, out ServerMap? map))
                return;

            await ForwardToAsync(connection, netMod, forwarded, PlayersOf(map), senderMod, excludeSender: true);
            break;
        }

        case PacketScope.Player:
        case PacketScope.Players:
            await ForwardToAsync(
                connection, netMod, forwarded, AddressedPlayers(netMod.Route.Targets), senderMod, excludeSender: false
            );
            break;

        default:
            throw new InvalidDataException(string.Format(
                CultureInfo.InvariantCulture, SR.UnknownPacketScope, (byte)netMod.Route.Scope
            ));
        }

        static IEnumerable<MiaoClientConnection> PlayersOf(IPlayerScope scope)
            => scope.Players;

        IEnumerable<MiaoClientConnection> AddressedPlayers(IReadOnlyList<int> targetPlayerIDs)
        {
            foreach (int targetPlayerID in targetPlayerIDs)
            {
                if (ServerState.Players.TryGetValue(targetPlayerID, out MiaoClientConnection? target))
                    yield return target;
            }
        }
    }

    private static async ValueTask ForwardToAsync(
        MiaoClientConnection sender,
        PacketNetMod netMod,
        PacketEnvelope envelope,
        IEnumerable<MiaoClientConnection> candidates,
        NetModInfo senderMod,
        bool excludeSender
    )
    {
        List<Task>? bounded = null;
        foreach (MiaoClientConnection target in candidates)
        {
            if (excludeSender && target.ID == sender.ID)
                continue;

            if (!target.TryGetNetModID(senderMod, out byte netModID))
                continue;

            PacketNetModDelivery forwarded = PacketNetModDelivery.From(netModID, netMod.LocalID, netMod.Payload);

            if (!target.TryQueuePacket(envelope, forwarded))
                (bounded ??= []).Add(target.QueuePacketAsync(envelope, forwarded).AsTask());
        }

        if (bounded is not null)
            await Task.WhenAll(bounded);
    }
}
