# MiaoNet.Shared 包系统

客户端和服务端使用 `source/MiaoNet.Shared` 中定义的同一套二进制协议。包 ID 注册在 `AssemblyInfo.cs` 的 `PacketRegistry` 属性中，由列表顺序决定；新增包只能追加到末尾。

## 线格式

```text
┌────────────┬────────────┬──────────────────┐
│ size u16 LE│ type u16 LE│ payload          │
│ 2 bytes    │ 2 bytes    │ size bytes       │
└────────────┴────────────┴──────────────────┘
```

`MiaoServerConnection` 使用 TLS 流读取完整 payload，再调用 `PacketRegistry` 的读取委托。payload 长度和类型均为 `ushort`，异常数据会包装为 `InvalidPacketDataException`。

## 接口和注册

```text
IContextualPacket
├── IContextualPacket<T>
├── IContextlessPacket
└── PacketRequest<TResponse>
    └── PacketResponse
```

- `IContextualPacket` 通过 `IPacketSerializationContext` 访问 `PooledStringManager`，用于 `PacketPlayerFrame` 等高频字符串包。
- `IContextlessPacket` 不依赖序列化上下文。
- `PacketRequest<TResponse>` 和 `PacketResponse` 通过 `RequestID` 配对。
- `PacketPlayerNotification<T>` 和 `PacketContextualPlayerNotification<T>` 为转发包附加发送者 `PlayerID`。

`PacketRegistry` 负责类型与 ID、序列化/反序列化委托的映射；`PacketHandlerRegister` 和 `PacketDispatcher` 按运行时类型调用客户端或服务端 handler。

## 序列化基础设施

`RefBinaryReader` / `RefBinaryWriter` 是基于 `ref struct` 的低分配读写器。实现 `IRefBinarySerializable<T>` 的值类型可以无上下文读写；包含动画名等高频字符串的类型使用 `IContextualRefBinarySerializable<T, PooledStringManager>`。`PooledStringManager` 在连接两端分别维护字符串与短 ID 的映射，`KnownPooledStrings` 预注册常用值。

## 当前包分类

### 连接和心跳

| 包 | 方向 | 作用 |
|---|---|---|
| `PacketClientInitial` | S->C | 握手完成后的自身信息、频道和在线玩家快照 |
| `PacketPlayerJoined` / `PacketPlayerLeft` | S->C | 玩家加入/离开 |
| `PacketDisconnected` | S->C | 断开原因 |
| `PacketPing` / `PacketPong` | 双向 | 心跳请求和响应 |
| `PacketPingData` | S->C | 同频道延迟数据 |

### 位置和同步

| 包 | 方向 | 作用 |
|---|---|---|
| `PacketPlayerFrame` | C->S | `PlayerStateDelta` 帧增量 |
| `PacketPlayerLocationChanged` | C->S | 进入/离开地图或切换房间，进入地图时附带初始状态 |
| `PacketPlayerLocationChangedNotification` | S->C | 位置 presence，或同地图带初始状态的通知 |
| `PacketPlayerLocationChangedResponse` | S->C | 请求者进入地图/Debug Map 时的同地图初始状态 |
| `PacketPlayerChannelMove` | C->S | 切换频道 |
| `PacketPlayerChannelMovedResponse` / `Notification` | S->C | 切频道快照和通知 |
| `PacketPlayerLiveState` | C->S | 死亡/复活 |
| `PacketPlayerGraphicsUpdate` | C->S | 头发等图形信息 |
| `PacketUpdateGlobalFlag` | C->S | 暂停、打字、直播、互动、合影、观战等标志 |

### 频道和聊天

| 包 | 方向 | 作用 |
|---|---|---|
| `PacketChannelCreateAndJoin` | C->S | 创建并加入频道 |
| `PacketChannelCreated` | S->C | 频道创建通知 |
| `PacketSendChatMessage` / `PacketChatMessage` | C->S / S->C | 全局、频道、地图聊天 |
| `PacketSendPrivateChatMessage` / `Response` | C->S / S->C | 私聊请求和结果 |

### 互动和表现

`PacketTeleportRequest` / `PacketTeleportResponse`、`PacketBeTeleportedRequest` / `Response` 用于两种传送模式；`PacketPlayerGrabPlayer`、`PacketPlayerGrabJumpOut` 用于抓取互动；`PacketSendEmote` / `PacketEmote` 和 `PacketSendEmoteText` / `PacketEmoteText` 用于表情；`PacketPlayerPlayedAudio` 同步音效；`PacketCreateFireworks` 创建烟花。

### 观战场景状态

`PacketWatchStart` / `Response` 建立由服务端确认的观战会话。服务端通过 `PacketWatchSnapshotRequest` / `Response` 向被观看方取得场景快照，随后将 `PacketWatchSceneDelta` 定向转发给该玩家的观看方。同一地图内切换房间不会结束会话；每次进入房间都会发送一份可为空的完整 Touch Switch 状态，防止沿用上次进入该房间时的缓存。场景增量携带产生它的 `PlayerLocation`，服务端只转发与生产者当前位置一致且序号连续的数据。

第一阶段的 `WatchSceneSnapshot` 包含 Session string flags 和当前房间已激活 Touch Switch 的 Entity ID；`WatchSceneDelta` 包含 flags 的增删，并可选择携带当前房间 Touch Switch 的完整替换状态。生产端因死亡重生、Retry 或读档在同一位置重新加载房间时，增量设置 `RequiresRoomReload` 并携带完整实体状态；观看端重载本地房间后再应用该状态，避免沿用上一房间实例的激活结果。实体状态只有在存在观看者时才采集和上传。`PacketWatchStop`、`PacketWatchProducerStop` 和 `PacketWatchEnded` 负责双方主动停止、生产端失败和服务端终止通知。所有场景状态包受协议 payload 上限约束。

## 修改协议

1. 在 `Packet/Packets/` 新增类型并实现对应序列化接口。
2. 在 `AssemblyInfo.cs` 的注册列表末尾追加类型。
3. 在客户端和服务端的 `RegisterPacketHandlers` 中注册需要处理的方向。
4. 为读写和 handler 添加单元测试，并至少构建 Client、Server 和 MockClient 的相关消费者。
