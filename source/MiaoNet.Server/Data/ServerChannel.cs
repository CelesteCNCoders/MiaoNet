using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using MiaoNet.Shared;

namespace MiaoNet.Server;

public sealed class ServerChannel
{
    private ImmutableDictionary<int, ServerState.Client> players;

    public ChannelStateInfo StateInfo { get; private set; }

    public ImmutableDictionary<int, ServerState.Client> Players { get => players; set => players = value; }

    public int ID => StateInfo.ID;

    public ServerChannel(ChannelStateInfo stateInfo)
    {
        players = ImmutableDictionary<int, ServerState.Client>.Empty;
        StateInfo = stateInfo;
    }

    public void OnAddPlayer(ServerPlayer player, MiaoClientConnection connection)
    {
        bool result = ImmutableInterlocked.Update(ref players, d => d.Add(player.ID, new(player, connection)));
        Debug.Assert(result);
    }

    public void OnRemovePlayer(ServerPlayer player)
    {
        bool result = ImmutableInterlocked.Update(ref players, d => d.Remove(player.ID));
        Debug.Assert(result);
    }
}