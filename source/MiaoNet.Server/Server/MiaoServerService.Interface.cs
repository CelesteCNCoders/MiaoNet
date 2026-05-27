using System;
using System.Collections.Generic;
using System.Text;
using MiaoNet.Shared;

namespace MiaoNet.Server;

partial class MiaoServerService : IMiaoServerService
{
    IReadOnlyDictionary<int, MiaoClientConnection> IMiaoServerService.Players 
        => ServerState.Players;

    IReadOnlyDictionary<int, ServerChannel> IMiaoServerService.Channels
        => ServerState.Channels;

    Task IMiaoServerService.BroadcastAsync(IContextlessPacket packet)
        => BroadcastAsync(packet);
}
