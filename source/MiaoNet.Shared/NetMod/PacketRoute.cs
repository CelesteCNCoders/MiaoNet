using System.Globalization;

namespace MiaoNet.Shared;

internal readonly struct PacketRoute
{
    public const int MaxTargets = 255;

    public PacketScope Scope { get; }

    public IReadOnlyList<int> Targets { get; }

    private PacketRoute(PacketScope scope, IReadOnlyList<int> targets)
    {
        Scope = scope;
        Targets = targets;
    }

    public static PacketRoute Broadcast(PacketScope scope)
    {
        if (!scope.IsBroadcast())
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture, SR.BroadcastScopeRequired, scope
            ));
        }

        return new(scope, []);
    }

    public static PacketRoute Unicast(int targetPlayerID)
        => new(PacketScope.Player, [targetPlayerID]);

    public static PacketRoute Multicast(IEnumerable<int> targetPlayerIDs)
    {
        var players = targetPlayerIDs.ToArray();

        if (players.Length > MaxTargets)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetPlayerIDs),
                players.Length,
                string.Format(CultureInfo.InvariantCulture, SR.TooManyMulticastTargets, MaxTargets)
            );
        }

        return new(PacketScope.Players, players);
    }

    public readonly void Write(ref RefBinaryWriter writer)
    {
        writer.Write((byte)Scope);
        switch (Scope)
        {
        case PacketScope.Player:
            writer.Write7BitEncodedInt(Targets[0]);
            break;

        case PacketScope.Players:
            writer.Write((byte)Targets.Count);
            foreach (int targetPlayerID in Targets)
                writer.Write7BitEncodedInt(targetPlayerID);
            break;
        }
    }

    public static PacketRoute Read(ref RefBinaryReader reader)
    {
        PacketScope scope = (PacketScope)reader.ReadByte();

        switch (scope)
        {
        case PacketScope.Player:
            return new(scope, [reader.Read7BitEncodedInt()]);

        case PacketScope.Players:
        {
            byte count = reader.ReadByte();
            int[] targets = new int[count];
            for (int i = 0; i < count; i++)
                targets[i] = reader.Read7BitEncodedInt();
            return new(scope, targets);
        }

        case PacketScope.Global:
        case PacketScope.Channel:
        case PacketScope.Map:
            return new(scope, []);

        default:
            throw new InvalidDataException(string.Format(
                CultureInfo.InvariantCulture, SR.UnknownPacketScope, (byte)scope
            ));
        }
    }

    public override string ToString()
        => Targets.Count == 0 ?
            Scope.ToString() :
            $"{Scope}[{string.Join(',', Targets)}]";
}
