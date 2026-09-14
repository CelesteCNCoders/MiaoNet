using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;

namespace Celeste.Mod.MiaoNet.UI.PlayerList;

// status icons shown after a player's name, in left-to-right order
[Flags]
public enum PlayerStatus
{
    None = 0,
    Paused = 1 << 0,
    Interactions = 1 << 1,
    LiveMode = 1 << 2,
    TakingGolden = 1 << 3,
    GroupPhotoMode = 1 << 4,
    Watching = 1 << 5,
}

// XNA-free view model of one row. every string is already resolved by the application layer:
// the adapter does live-mode masking, clipping and localization before building these.
public sealed class PlayerRow
{
    public required string DisplayName { get; init; }

    public UiColor NameColor { get; init; } = UiColor.White;

    public PlayerStatus Status { get; init; }

    // null when the ping is unknown, and nothing is drawn for it
    public string? PingText { get; init; }

    public bool HasLocation { get; init; }

    // room or chapter text, null when UsesDebugRoomIcon is set
    public string? RoomText { get; init; }

    // true when the room column is the debug-map icon instead of text
    public bool UsesDebugRoomIcon { get; init; }

    public string? MapName { get; init; }

    public UiColor MapNameColor { get; init; } = UiColor.LightGray;

    public string? AreaModeText { get; init; }

    public UiColor MapSideColor { get; init; } = UiColor.LightGray;

    public IUiTexture? AreaIcon { get; init; }
}

// one channel section of the player list
public sealed class PlayerListChannel
{
    public required string Header { get; init; }

    public IReadOnlyList<PlayerRow> Rows { get; init; } = [];
}

// icon textures supplied by the application layer
public sealed class PlayerListIcons
{
    public IUiTexture? Paused { get; init; }

    public IUiTexture? Interactions { get; init; }

    public IUiTexture? LiveMode { get; init; }

    public IUiTexture? TakingGolden { get; init; }

    public IUiTexture? GroupPhotoMode { get; init; }

    // reused for both the watching flag and the debug-map room marker
    public IUiTexture? DebugMap { get; init; }

    public IUiTexture? For(PlayerStatus status) => status switch
    {
        PlayerStatus.Paused => Paused,
        PlayerStatus.Interactions => Interactions,
        PlayerStatus.LiveMode => LiveMode,
        PlayerStatus.TakingGolden => TakingGolden,
        PlayerStatus.GroupPhotoMode => GroupPhotoMode,
        PlayerStatus.Watching => DebugMap,
        _ => null,
    };
}
