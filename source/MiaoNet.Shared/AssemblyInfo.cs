using MiaoNet.Shared;

[assembly: PacketRegistry([
    typeof(PacketClientInitial),
    typeof(PacketPlayerJoined),
    typeof(PacketPlayerLeft),

    typeof(PacketPlayerFrame),
    typeof(PacketContextualPlayerNotification<PacketPlayerFrame>),
    typeof(PacketPlayerLiveState),
    typeof(PacketPlayerNotification<PacketPlayerLiveState>),

    typeof(PacketPlayerMapChanged),
    typeof(PacketPlayerMapChangedNotification),
    typeof(PacketPlayerMapChangedResponse),

    typeof(PacketPlayerMapRoomChanged),
    typeof(PacketPlayerNotification<PacketPlayerMapRoomChanged>),

    typeof(PacketPlayerChannelMove),
    typeof(PacketPlayerNotification<PacketPlayerChannelMove>),

    typeof(PacketChatMessage),
    typeof(PacketSendChatMessage),

    typeof(PacketEmote),
    typeof(PacketSendEmote),
    typeof(PacketEmoteText),
    typeof(PacketSendEmoteText),

    typeof(PacketUpdateOnlineStatus),
    typeof(PacketPlayerNotification<PacketUpdateOnlineStatus>),

    typeof(PacketTeleportRequest),
    typeof(PacketTeleportResponse),
    typeof(PacketBeTeleportedRequest),
    typeof(PacketBeTeleportedResponse),

    typeof(PacketSendPrivateChatMessage),
    typeof(PacketSendPrivateChatMessageResponse),

    typeof(PacketPing),
    typeof(PacketPong),
    typeof(PacketPingData),

    typeof(PacketDisconnected),

    typeof(PacketPlayerPlayedAudio),
    typeof(PacketContextualPlayerNotification<PacketPlayerPlayedAudio>),

    typeof(PacketPlayerGrabPlayer),
    typeof(PacketPlayerGrabJumpOut),

    typeof(PacketCreateFireworks),
    typeof(PacketPlayerNotification<PacketCreateFireworks>)
])]
