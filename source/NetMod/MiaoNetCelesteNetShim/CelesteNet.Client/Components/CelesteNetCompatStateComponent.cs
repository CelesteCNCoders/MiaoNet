using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.MiaoNet;
using Celeste.Mod.MiaoNetCelesteNetShim;
using Microsoft.Xna.Framework;
using MiaoNet.Shared;

namespace Celeste.Mod.CelesteNet.Client.Components;

/// <summary>
/// Bridges MiaoNet state into the CelesteNet shaped view that mods consume: the local
/// player info, the player reference table used by meta types, the channel list and the
/// connect/disconnect lifecycle of the client.
/// </summary>
internal sealed class CelesteNetCompatStateComponent : CelesteNetGameComponent
{
    private readonly Dictionary<int, DataPlayerInfo> knownPlayers = new();

    private bool announced;

    public CelesteNetCompatStateComponent(CelesteNetClientContext context, Game game) : base(context, game) { }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        CelesteNetClient? client = Client;
        if (client is null || Context is null)
            return;

        // Never creates the context: the shim has nothing to do until the player connects,
        // and MiaoNet's context cannot be built before the game has loaded anyway.
        MiaoNetContext? miaoNet = MiaoNetModule.Instance?.MiaoNetContextOptional;
        if (miaoNet is null || !miaoNet.HasConnection)
        {
            if (announced)
                Disconnect(client);
            return;
        }

        if (!announced)
        {
            // The player table has to exist before the client starts: mods react to OnStart
            // by sending immediately, and every packet carries the local player in its meta.
            // HasConnection already guarantees a client state.
            SyncState(miaoNet, client);

            announced = true;
            Context.Start();
            CelesteNetCompatBackend.Diag("MiaoNet session up; the compat client started.");
        }

        SyncState(miaoNet, client);
    }

    private void Disconnect(CelesteNetClient client)
    {
        announced = false;
        knownPlayers.Clear();
        CelesteNetCompatBackend.Diag("MiaoNet session down; the compat client stopped.");
        // Raises OnDisconnect for the mods which subscribed to it.
        client.Stop();
    }

    private void SyncState(MiaoNetContext miaoNet, CelesteNetClient client)
    {
        ClientState? state = miaoNet.ClientState;
        if (state is null)
            return;

        client.PlayerInfo = GetOrCreatePlayerInfo(client, state.Self);
        foreach (OnlinePlayer player in state.Players.Values)
            GetOrCreatePlayerInfo(client, player);

        CelesteNetPlayerListComponent? playerList = Context?.Get<CelesteNetPlayerListComponent>();
        if (playerList is not null)
            playerList.Channels = BuildChannelList(state);
    }

    /// <summary>
    /// Keeps one stable <see cref="DataPlayerInfo"/> per online player. Meta types only
    /// carry a player id, so this table is what lets <c>FixupMeta</c> resolve a sender.
    /// The id is the MiaoNet player id, which is also what CoopHelper treats as the
    /// CelesteNet player id.
    /// </summary>
    private DataPlayerInfo GetOrCreatePlayerInfo(CelesteNetClient client, OnlinePlayer player)
    {
        if (!knownPlayers.TryGetValue(player.ID, out DataPlayerInfo? info))
        {
            info = new DataPlayerInfo { ID = (uint)player.ID };
            knownPlayers[player.ID] = info;
            CelesteNetCompatBackend.Diag($"Player {player.ID} '{player.Info.Name}' joined.");
        }

        PlayerInfo source = player.Info;
        info.Name = source.Name;
        info.FullName = source.DisplayName;
        info.DisplayName = source.DisplayName;
        info.Prefix = source.Prefix;
        info.NameColor = source.Color;

        client.Data.RegisterRef(info.ID, info);
        return info;
    }

    private static DataChannelList BuildChannelList(ClientState state)
    {
        List<DataChannelList.Channel> channels = [];

        foreach (OnlineChannel channel in state.Channels.Values)
        {
            // The private channel is a client side virtual placeholder, not a real channel.
            if (channel.ID == ChannelInfo.PrivateChannelVirtualID)
                continue;

            HashSet<uint> players = [.. channel.Players.Select(player => (uint)player.ID)];
            // A channel's player set never contains self, but mods search for their own id.
            if (channel.ID == state.SelfChannel.ID)
                players.Add((uint)state.Self.ID);

            channels.Add(new DataChannelList.Channel
            {
                Name = channel.Info.Name,
                ID = (uint)channel.ID,
                Players = [.. players],
            });
        }

        return new DataChannelList { List = [.. channels] };
    }
}
