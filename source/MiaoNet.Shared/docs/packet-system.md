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
| `PacketClientInitial` | S->C | 握手完成后的自身信息、频道、在线玩家快照和可选服务端能力尾字段 |
| `PacketPlayerJoined` / `PacketPlayerLeft` | S->C | 玩家加入/离开 |
| `PacketDisconnected` | S->C | 断开原因 |
| `PacketPing` / `PacketPong` | 双向 | 心跳请求和响应 |
| `PacketPingData` | S->C | 同频道延迟数据 |

### 位置和同步

| 包 | 方向 | 作用 |
|---|---|---|
| `PacketPlayerFrame` | C->S | 带 Player Epoch/Sequence 的 Delta 或完整 Keyframe |
| `PacketPlayerLocationChanged` | C->S | PlayerTimeline 世代屏障；进入地图和切换房间均附带完整基线 |
| `PacketPlayerLocationChangedNotification` | S->C | 位置 presence，或同地图带初始状态的通知 |
| `PacketPlayerLocationChangedResponse` | S->C | 请求者进入地图/Debug Map 时的同地图初始状态 |
| `PacketPlayerChannelMove` | C->S | 按名称切换频道；不存在时由服务端创建 |
| `PacketPlayerChannelMovedResponse` / `Notification` | S->C | 切频道快照和通知 |
| `PacketPlayerLiveState` | C->S | 死亡、实际死亡 WipeOut 起点与复活 |
| `PacketPlayerGraphicsUpdate` | C->S | 头发等图形信息 |
| `PacketUpdateGlobalFlag` | C->S | 暂停、打字、直播、互动、合影、观战等标志 |

### 频道和聊天

| 包 | 方向 | 作用 |
|---|---|---|
| `PacketChannelCreated` | S->C | 频道创建通知 |
| `PacketSendChatMessage` / `PacketChatMessage` | C->S / S->C | 全局、频道、地图聊天 |
| `PacketSendPrivateChatMessage` / `Response` | C->S / S->C | 私聊请求和结果 |

### 互动和表现

`PacketTeleportRequest` / `PacketTeleportResponse`、`PacketBeTeleportedRequest` / `Response` 用于两种传送模式；`PacketPlayerGrabPlayer`、`PacketPlayerGrabJumpOut` 用于抓取互动；`PacketSendEmote` / `PacketEmote` 和 `PacketSendEmoteText` / `PacketEmoteText` 用于表情；`PacketPlayerPlayedAudio` 同步音效；`PacketCreateFireworks` 创建烟花。

### 观战场景状态

`PacketClientInitial` 在旧字段之后可选附加 `ServerFeatureFlags`；新客户端在尾部不存在时将其视为 `None`，旧客户端则忽略尾字段。客户端通过 `PlayerGlobalFlags.WatchSceneSyncSupported` 公告能力。只有 Server、Watcher 和 Player 三方都支持 `WatchSceneSync` 时，Watcher 才发送 `PacketWatchStart`；否则使用原始本地观看流程，不向旧服务端发送未注册的 Watch 包。

`PacketWatchStart` / `Response` 建立由服务端确认的观战会话。服务端通过 `PacketWatchSnapshotRequest` / `Response` 向被观看方取得场景快照，随后将 `PacketWatchSceneDelta` 定向转发给该玩家的观看方。Snapshot/Delta 同时携带 Player Epoch 与 Player Sequence 水位；新世代只有在对应的 Scene Replace 到达后才允许 Watcher 应用该世代 Player 帧。同一地图内切换房间不会结束会话；Player 实际触发原版 `Level.TransitionTo` 时，生产端记录 source、target、出口位置和原版方向，Watcher 等待同序目标房间完整状态后以这些元数据启动平滑转场。超过 8 KiB 的 Scene 状态先发送 `PacketWatchSceneTransferStart`，再拆成最多 8 个 8 KiB `PacketWatchSceneChunk`；Start Descriptor 携带传输种类、目标玩家及 Player 水位，Chunk 只携带 TransferID、索引和数据。接收方核对长度、Scene Sequence、Player Epoch 和水位，只在完整重组后发布逻辑包。目标离线或 Watch 生命周期结束时，客户端清除普通目标传输，但保留已开始的 StartResponse，使连接仍有效时 `pendingRequests` 能完成并由现有回调拒绝已失效目标；被清除传输的后续 Chunk 因不存在 Pending 而安全忽略。超时、冲突、重复和越界分片不会应用半包。发现 Scene 缺序时暂停受影响 Session 并请求共享快照；Player 帧缺序则等待同 Epoch Keyframe 恢复。

`WatchSceneSnapshot` 包含 Session string flags 和已注册实体适配器的完整状态；`WatchSceneDelta` 包含 flags 增删、实体状态补丁或完整替换，以及有序的瞬时实体事件。Player 在 `PlayerDeadBody.End` 即将调用原版 `DoScreenWipe` 时先向实际 Watcher 定向发送 `DeathWipe`，Watcher 据此同步开始 WipeOut，旧客户端不会收到该新枚举值。生产端在普通死亡或 Retry 后等待 Player 重生，再发送带 `IsDeathRespawn`、不带 `RequiresRoomReload` 的完整状态；若死亡流程直接进入另一个房间，目标房间的完整 `Replace` 本身携带该标记。Watcher 在缩圈过程中继续接收并缓存状态；如果快照略晚，WipeOut 会停在完全黑屏帧，直到能够原子应用完整实体状态和周期相位，然后直接整屏亮起，不额外播放 WipeIn。正常情况保留当前 Level、Camera、背景和音乐；Touch Switch、Temple Cracked Block、Final Boss Moving Block 及其地图 Spikes 等单向状态在黑屏帧由对应适配器定向重建。适配器无法逆转的局部状态只按实体类型记录诊断，不能把轻量死亡升级为整房重载。生产端只有实际发生 F5、读档或其他显式完整生命周期时才设置 `RequiresRoomReload`，Watcher 也只有收到该标记后才调用 `Level.Reload()`。真实切房继续使用原版 `Level.TransitionTo` 的 Camera 动画；目标房间加载完成后即在转场过程中应用完整快照，隐藏的本地 Player 不参与原版转场移动完成判定。死亡后直接换房的特殊流程会保留死亡与复活通知，等目标房间就绪后立即恢复 Ghost。正常同房观看时，Player 将最终 `Camera.Position` 作为可选字段附加到现有 `PacketPlayerFrame`，Watcher 只对该权威坐标做短时插值，不再同时运行基于 Player 坐标的独立镜头控制；服务端只向该 Player 的活动 Watcher 保留 Camera 字段，其他同地图客户端仍接收去除 Camera 字段的普通 PlayerFrame；转场期间只缓存 Camera，完全黑屏帧允许直接重锚。实体状态只有在存在观看者时才采集和上传；单个适配器失败或一次聚合增量无效时，生产端会隔离、沿用最后有效状态或跳过该次更新，不再主动结束 Watch 会话。`PacketWatchStop`、`PacketWatchProducerStop` 和 `PacketWatchEnded` 只负责双方主动停止、无法继续生产的生命周期失败和服务端终止通知。所有场景状态包仍受协议 payload 上限约束。

持久实体适配使用 `PersistentSession` 状态同步当前房间的收集与永久移除集合，并用独立的 Checkpoint 状态同步普通和 Summit 检查点。观看端会临时覆盖这些 Session 字段，在停止观战时恢复进入观战前的副本并按需重载当前房间；服务端仍只验证共享 payload 的格式、大小和序号。

即时表现实体继续复用同一状态和事件通道：Spring、Fly Feather、可重复 `FakeHeart` 与 Crumble Platform 使用有序事件重放短动画，Bumper 事件携带碰撞方向和节点位置；Refill、Fly Feather、FakeHeart、Booster、Cloud、Dash Switch、Temple Gate、Core Mode 和 Heart Gem Door 使用权威状态补丁收敛。Booster 状态还携带泡泡渲染位置和明确的进入、冲出、破裂、重生阶段；Heart Gem Door 状态包含完整的可见性与渲染进度。永久与非永久 Dash Block 均通过存在状态和携带破坏方向的事件同步；草莓籽单独同步 ghost 外观、跟随、返回、合并阶段及父草莓等待状态。`MovingSolid` 使用固定 24 字节状态同步除 Bounce Block 外的 11 类移动方块；Bounce Block 改用独立完整状态和单次破坏事件同步冷热模式及碎片方向。

周期平台状态覆盖 Moving Platform、`rotatingPlatforms` 生成的子平台、Slider、Track Spinner 与 Rotate Spinner；后三者采用本地演算加低频权威锚点。Cassette Block 同步 Manager 节拍、各方块激活阶段、绝对位置和碰撞高度，Watcher 不再自行推进节拍。`TouchSwitchAndSwitchGate` 用一个实体类别承载两种状态：Touch Switch 使用房间级激活 ID 集合，Switch Gate 仍逐实体同步绝对位置、碰撞和图标动画。Clutter 状态族覆盖 Color Switch、Cabinet、Door、红绿黄三组的存在状态，以及当前与 Player 直接接触的 Clutter 根块；整组清理使用有序事件重放吸收碎片、同色基底停用和柜门关闭续程，Watcher 对接触根块调用原版 `WeightDown()`，不传输逐块浮动计时。普通 Door、Trapdoor 与 MrOshiroDoor 同步持续状态，并使用有序事件重放开启方向和动画。实体消失会触发完整替换；适配器发现本地实体集合不一致时只记录诊断，不得自行把普通 Replace 升级为房间重载。服务端仅校验各类 payload 的长度、枚举范围和有限浮点值，不执行任何 Celeste 实体逻辑。

Key、Lock Block、Theo Crystal、Glider、Theo Crystal Pedestal、Badeline Boost、Fling Bird、Wall Booster、Torch、Temple Cracked Block 和 Temple Big Eyeball 复用同一实体状态/事件通道。Theo Crystal 与 Glider 共用离散 Holdable 阶段；Player 手持外观继续由既有 PlayerFrame 数据传输，携带阶段不发送位置，投掷、移动和飞行阶段只发送低频绝对校正，释放事件携带位置和 force。Player 状态位额外携带跨房仍有效的红 Booster 状态；Watcher 用它保持 PlayerSprite 的原版泡泡动画，不创建第二套 Sprite，也不在本地启动 Booster 协程。观看副本 Theo 与 HeartGem 的碰撞不会启动本地收集协程，最终存在状态仍由 `PersistentSession` 收敛。Badeline Boost 与 Summit 上升过场动态创建的 `BadelineDummy` 使用独立运行时 ID 和低频位置/动画锚点；Ascend 背景的 Streaks/Clouds 在 Watcher 本地按权威 Manager 参数生成，`HeightDisplay` 则按生产端存在性和动画参数同步。所有这些副本都禁用 Player、Camera、Session 与音频进度副作用。Badeline Boost、Fling Bird 与 Temple Big Eyeball 的事件不会在 Watcher 端启动会控制 Player 或完成章节的 cutscene 协程。

## 修改协议

1. 在 `Packet/Packets/` 新增类型并实现对应序列化接口。
2. 在 `AssemblyInfo.cs` 的注册列表末尾追加类型。
3. 在客户端和服务端的 `RegisterPacketHandlers` 中注册需要处理的方向。
4. 为读写和 handler 添加单元测试，并至少构建 Client、Server 和 MockClient 的相关消费者。
