using MiaoNet.Shared;

[assembly: PacketRegistry([
    typeof(PacketClientInitial),
    typeof(PacketPlayerJoined),
    typeof(PacketPlayerLeft),

    typeof(PacketPlayerFrame),
    typeof(PacketPlayerLiveState),

    typeof(PacketPlayerLocationChanged),
    typeof(PacketPlayerLocationChangedNotification),
    typeof(PacketPlayerLocationChangedResponse),

    typeof(PacketChatMessage),
    typeof(PacketSendChatMessage),

    typeof(PacketEmote),
    typeof(PacketEmoteText),

    typeof(PacketUpdateGlobalFlag),

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

    typeof(PacketPlayerGrabPlayer),
    typeof(PacketPlayerGrabJumpOut),

    typeof(PacketCreateFireworks),

    typeof(PacketPlayerChannelMove),
    typeof(PacketPlayerChannelMovedResponse),
    typeof(PacketPlayerChannelMovedNotification),
    typeof(PacketChannelCreated)
])]
