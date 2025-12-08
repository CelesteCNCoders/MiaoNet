using MiaoNet.Shared;

[assembly: PacketRegistry(typeof(PacketClientInitial))]
[assembly: PacketRegistry(typeof(PacketPlayerJoined))]
[assembly: PacketRegistry(typeof(PacketPlayerLeft))]

[assembly: PacketRegistry(typeof(PacketPlayerFrame))]
[assembly: PacketRegistry(typeof(PacketPlayerFrameNotification))]

[assembly: PacketRegistry(typeof(PacketPlayerMapChanged))]
[assembly: PacketRegistry(typeof(PacketPlayerMapChangedNotification))]
[assembly: PacketRegistry(typeof(PacketPlayerMapChangedResponse))]

[assembly: PacketRegistry(typeof(PacketPlayerMapRoomChanged))]
[assembly: PacketRegistry(typeof(PacketPlayerMapRoomChangedNotification))]

[assembly: PacketRegistry(typeof(PacketPlayerChannelMove))]
[assembly: PacketRegistry(typeof(PacketPlayerChannelMoveNotification))]

[assembly: PacketRegistry(typeof(PacketChatMessage))]
[assembly: PacketRegistry(typeof(PacketSendChatMessage))]

[assembly: PacketRegistry(typeof(PacketChatCommand))]
[assembly: PacketRegistry(typeof(PacketChatCommandResponse))]

[assembly: PacketRegistry(typeof(PacketEmote))]
[assembly: PacketRegistry(typeof(PacketSendEmote))]
//[assembly: PacketRegistry(typeof(PacketEmoteText))]
//[assembly: PacketRegistry(typeof(PacketSendEmoteText))]