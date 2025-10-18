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