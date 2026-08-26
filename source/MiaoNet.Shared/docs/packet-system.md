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
| `PacketPlayerLiveState` | C->S | 死亡、实际死亡 WipeOut 起点与复活 |
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

`PacketWatchStart` / `Response` 建立由服务端确认的观战会话。服务端通过 `PacketWatchSnapshotRequest` / `Response` 向被观看方取得场景快照，随后将 `PacketWatchSceneDelta` 定向转发给该玩家的观看方。同一地图内切换房间不会结束会话；Player 实际触发原版 `Level.TransitionTo` 时，生产端记录 source、target、出口位置和原版方向，Watcher 等待同序目标房间完整状态后以这些元数据启动平滑转场，不再通过房间矩形边界猜测方向。没有原版转场事件的生命周期跳转才使用传送兜底。每次进入房间都会发送可为空的完整 Touch Switch 和已适配实体状态，防止沿用上次进入该房间时的缓存。场景增量携带产生它的 `PlayerLocation`，服务端只转发与生产者当前位置一致且序号连续的数据。

`WatchSceneSnapshot` 包含 Session string flags、当前房间已激活 Touch Switch 的 Entity ID 和已注册实体适配器的完整状态；`WatchSceneDelta` 包含 flags 增删、Touch Switch 完整替换、实体状态补丁或完整替换，以及有序的瞬时实体事件。Player 在 `PlayerDeadBody.End` 即将调用原版 `DoScreenWipe` 时先发送 `DeathWipe`，Watcher 据此同步开始 WipeOut，而不是等到复活后推测时刻。生产端在普通死亡或 Retry 后等待 Player 重生，再发送带 `IsDeathRespawn`、不带 `RequiresRoomReload` 的完整状态；若死亡流程直接进入另一个房间，目标房间的完整 `Replace` 本身携带该标记。Watcher 在缩圈过程中继续接收并缓存状态；如果快照略晚，WipeOut 会停在完全黑屏帧，直到能够原子应用完整 Touch Switch、实体状态和周期相位，然后直接整屏亮起，不额外播放 WipeIn。正常情况保留当前 Level、Camera、背景和音乐；Touch Switch 与缺失的 Temple Cracked Block 会在黑屏帧按地图 ID 定向重建，若其他单向实体仍明确要求重建，则在同一黑屏帧仅执行一次 `Level.Reload()` 并重新应用完整快照。生产端只有 F5、读档或其他显式完整生命周期才设置 `RequiresRoomReload`。真实切房继续使用原版 `Level.TransitionTo` 的 Camera 动画；目标房间加载完成后即在转场过程中应用完整快照，隐藏的本地 Player 不参与原版转场移动完成判定。死亡后直接换房的特殊流程会保留死亡与复活通知，等目标房间就绪后立即恢复 Ghost。正常同房观看时，Player 将最终 `Camera.Position` 作为可选字段附加到现有 `PacketPlayerFrame`，Watcher 只对该权威坐标做短时插值，不再同时运行基于 Player 坐标的独立镜头控制；转场期间只缓存 Camera，完全黑屏帧允许直接重锚。实体状态只有在存在观看者时才采集和上传。`PacketWatchStop`、`PacketWatchProducerStop` 和 `PacketWatchEnded` 负责双方主动停止、生产端失败和服务端终止通知。所有场景状态包受协议 payload 上限约束。

持久实体适配使用 `PersistentSession` 状态同步当前房间的收集与永久移除集合，并用独立的 Checkpoint 状态同步普通和 Summit 检查点。观看端会临时覆盖这些 Session 字段，在停止观战时恢复进入观战前的副本并按需重载当前房间；服务端仍只验证共享 payload 的格式、大小和序号。

即时表现实体继续复用同一状态和事件通道：Spring、Fly Feather、可重复 `FakeHeart` 与 Crumble Platform 使用有序事件重放短动画，Bumper 事件携带碰撞方向和节点位置；Refill、Fly Feather、FakeHeart、Booster、Cloud、Dash Switch、Temple Gate、Core Mode 和 Heart Gem Door 使用权威状态补丁收敛。Booster 状态还携带泡泡渲染位置和明确的进入、冲出、破裂、重生阶段；Heart Gem Door 状态包含完整的可见性与渲染进度。永久与非永久 Dash Block 均通过存在状态和携带破坏方向的事件同步；草莓籽单独同步 ghost 外观、跟随、返回、合并阶段及父草莓等待状态。`MovingSolid` 使用固定 24 字节状态同步除 Bounce Block 外的 11 类移动方块；Bounce Block 改用独立完整状态和单次破坏事件同步冷热模式及碎片方向。

周期平台状态覆盖 Moving Platform、`rotatingPlatforms` 生成的子平台、Slider、Track Spinner 与 Rotate Spinner；后三者采用本地演算加低频权威锚点。Cassette Block 同步 Manager 节拍、各方块激活阶段、绝对位置和碰撞高度，Watcher 不再自行推进节拍。Switch Gate 同步绝对位置、碰撞和图标动画。Clutter 状态族覆盖 Color Switch、Cabinet、Door、红绿黄三组的存在状态，以及当前与 Player 直接接触的 Clutter 根块；整组清理使用有序事件重放吸收碎片、同色基底停用和柜门关闭续程，Watcher 对接触根块调用原版 `WeightDown()`，不传输逐块浮动计时。普通 Door、Trapdoor 与 MrOshiroDoor 同步持续状态，并使用有序事件重放开启方向和动画。实体消失会触发完整替换；适配器发现本地实体集合不一致时只记录诊断，不得自行把普通 Replace 升级为房间重载。服务端仅校验各类 payload 的长度、枚举范围和有限浮点值，不执行任何 Celeste 实体逻辑。

Key、Lock Block、Theo Crystal、Glider、Theo Crystal Pedestal、Badeline Boost、Fling Bird、Wall Booster、Torch、Temple Cracked Block 和 Temple Big Eyeball 复用同一实体状态/事件通道。Theo Crystal 与 Glider 共用离散 Holdable 阶段；Player 手持外观继续由既有 PlayerFrame 数据传输，携带阶段不发送位置，投掷、移动和飞行阶段只发送低频绝对校正，释放事件携带位置和 force。Player 状态位额外携带跨房仍有效的红 Booster 状态；Watcher 用它保持 PlayerSprite 的原版泡泡动画，不创建第二套 Sprite，也不在本地启动 Booster 协程。观看副本 Theo 与 HeartGem 的碰撞不会启动本地收集协程，最终存在状态仍由 `PersistentSession` 收敛。Badeline Boost、Fling Bird 与 Temple Big Eyeball 的事件不会在 Watcher 端启动会控制 Player 或完成章节的 cutscene 协程。

## 修改协议

1. 在 `Packet/Packets/` 新增类型并实现对应序列化接口。
2. 在 `AssemblyInfo.cs` 的注册列表末尾追加类型。
3. 在客户端和服务端的 `RegisterPacketHandlers` 中注册需要处理的方向。
4. 为读写和 handler 添加单元测试，并至少构建 Client、Server 和 MockClient 的相关消费者。
