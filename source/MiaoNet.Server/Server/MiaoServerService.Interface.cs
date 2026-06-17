using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using MiaoNet.Shared;

namespace MiaoNet.Server;

partial class MiaoServerService : IMiaoServerService
{
    IReadOnlyDictionary<int, MiaoClientConnection> IMiaoServerService.Players
        => ServerState.Players;

    IReadOnlyDictionary<int, IChannelView> IMiaoServerService.Channels
        => new ChannelViewAdapter(ServerState);

    Task IMiaoServerService.BroadcastAsync(IContextlessPacket packet)
        => BroadcastAsync(packet);

    private readonly struct ChannelViewAdapter(ServerState state) : IReadOnlyDictionary<int, IChannelView>
    {
        public IChannelView this[int key] => state.Channels[key];
        public IEnumerable<int> Keys => state.Channels.Keys;
        public IEnumerable<IChannelView> Values => state.Channels.Values;
        public int Count => state.Channels.Count;
        public bool ContainsKey(int key) => state.Channels.ContainsKey(key);
        public bool TryGetValue(int key, [MaybeNullWhen(false)] out IChannelView value)
        {
            if (state.Channels.TryGetValue(key, out var channel))
            {
                value = channel;
                return true;
            }
            value = null;
            return false;
        }
        public IEnumerator<KeyValuePair<int, IChannelView>> GetEnumerator()
        {
            foreach (var kvp in state.Channels)
                yield return new KeyValuePair<int, IChannelView>(kvp.Key, kvp.Value);
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
