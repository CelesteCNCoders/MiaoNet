using MiaoNet.Shared;

[assembly: PacketRegistry(typeof(PacketClientInitial))]
[assembly: PacketRegistry(typeof(PacketPlayerJoined))]
[assembly: PacketRegistry(typeof(PacketPlayerLeft))]

[assembly: PacketRegistry(typeof(PacketPlayerFrame))]
[assembly: PacketRegistry(typeof(PacketPlayerFrameNotify))]

[assembly: PacketRegistry(typeof(PacketPlayerMapChanged))]
[assembly: PacketRegistry(typeof(PacketPlayerMapChangedNotify))]

[assembly: PacketRegistry(typeof(PacketPlayerMapRoomChanged))]
[assembly: PacketRegistry(typeof(PacketPlayerMapRoomChangedNotify))]

[assembly: PacketRegistry(typeof(PacketPlayerChannelMove))]
[assembly: PacketRegistry(typeof(PacketPlayerChannelMoveNotify))]