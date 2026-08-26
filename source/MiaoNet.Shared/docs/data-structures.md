# MiaoNet.Shared 数据结构

共享数据位于 `source/MiaoNet.Shared/Data/`，用于描述玩家身份、位置、频道、实时状态和传送 Session。可序列化类型实现 `IRefBinarySerializable<T>` 或带 `PooledStringManager` 的 contextual 接口。

## 玩家身份与在场状态

`PlayerInfo` 在握手时确定，包含 `AuthID`、显示名、前缀、头像 URL 和颜色。`PlayerGlobalFlags` 是实时广播的位标志：`Paused`、`Typing`、`LiveMode`、`Interactions`、`TakingGolden`、`GroupPhotoMode` 和 `Watching`。

`PlayerPresenceData` 将 `PlayerLocation` 与 `PlayerGlobalFlags` 打包；`PlayerPresenceDataWithID` 额外带玩家 ID，供频道切换和快照使用。

## 位置

```csharp
public readonly struct PlayerMapLocation
{
    string Sid;          // 地图 SID
    AreaMode AreaMode;   // Normal / BSide / CSide
}

public readonly struct PlayerLocation
{
    PlayerMapLocation Map;
    string Room;
}
```

`PlayerLocation.Empty` 表示不在地图中；地图非空而 `Room` 为空表示 Debug Map；地图和房间都存在表示正常关卡。`GetChangeResult` 返回 `None`、`Incremental` 或 `FullSync`，客户端据此决定仅更新位置还是重建 Ghost。

## 实时状态

`PlayerState` 是可克隆的完整状态，包含位置、动画/帧、缩放、`PlayerStateFlags`、冲刺数、帧时间、精灵模式、Follower、风向和持有物。`PlayerStateDelta` 是每帧使用的增量，`FrameFlags` 控制是否携带冲刺、持有物、Follower 初始/增量和风向。服务端在 `ServerMap.StateLock` 下将增量应用到玩家状态。

附属结构包括：

- `FollowerInfo` / `FollowerInfoDelta`：Follower 类型、Sprite、动画、帧和偏移；
- `HoldableInfo`：持有物类型、偏移、动画、缩放和旋转；
- `PlayerGraphicsInfo` / `HairInfo`：不同冲刺状态下的头发配置；
- `PlayerPlayedAudio`：要同步播放的音效和参数。

## 频道和聊天

`ChannelInfo` 仅保存频道名称；`ChatChannel` 有 `Global`、`Channel`、`Map` 三种发送范围。服务端将 `PacketSendChatMessage` 转换成带时间、类型、发送者 ID 和内容的 `PacketChatMessage`。私聊使用单独的请求/响应包。

## 传送

`PlayerSessionData` 保存创建 Celeste `Session` 所需的地图进度、背包、草莓、旗标、核心模式、时间、复活点和颜色设置。`PacketTeleportResponse` 可携带此数据；客户端的无 Session 模式只使用目标位置，带 Session 模式会重建目标玩家的 Session。

## 初始状态包装

`PlayerMovedInitialData` 包装完整 `PlayerState`；`PlayerMovedInitialDataWithID` 再附加玩家 ID。`PacketClientInitial`、频道切换响应和位置切换响应使用这些结构发送同地图玩家的初始状态，避免新建 Ghost 时等待下一帧。

## 观战场景状态

`WatchSceneSnapshot` 保存快照位置、序号、Session string flags，以及当前房间已激活 Touch Switch 的 Entity ID。`WatchSceneDelta` 保存产生变化时的位置、连续序号、flags 增删，并用 `HasTouchSwitchState` 区分“未更新此类状态”和“用空集合替换状态”。Touch Switch 集合始终表示该房间的完整激活状态，而不是单个触发事件，因此切房和重复进入房间可以丢弃旧缓存。`RequiresRoomReload` 表示生产端在相同位置重新建立了房间实例；该增量必须携带完整 Touch Switch 状态，观看端重载对应房间后再应用它。

## 约定

- 高速路径中的动画字符串使用 `PooledStringManager`，不要在每帧协议中引入新的普通字符串字段。
- 可选的运行时状态用 nullable 明确表达，例如 Debug Map 中 `PlayerState` 可能为空。
- 修改共享结构时同步更新包注册、客户端/服务端 handler 和序列化测试。
