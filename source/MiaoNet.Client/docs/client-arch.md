# MiaoNet 客户端架构

`MiaoNet.Client` 是 Everest Mod 项目，目标框架为 `net8.0`。它通过 `CelesteMod.props` 引用本地 Celeste/Everest 程序集，并把 `MiaoNet.Shared`、`MiaoNet.ClientShared` 和 `ChatInputBox` 的源码链接进客户端程序集。

## 目录

```text
source/MiaoNet.Client/
├── Game/              Everest 入口、Hook、设置、字体、资源和控制台命令
├── Connection/        MiaoNetContext、连接线程、包分发和连接状态
├── Components/        同步、聊天、表情、玩家列表、Debug Map、状态 UI
├── Data/              ClientState、OnlinePlayer/Channel、聊天消息和传送数据
├── Entity/             Ghost、名称标签、表情、烟花、合影平台等游戏实体
├── Command/            聊天命令定义、解析、参数类型和执行上下文
├── ClientRC/           OAuth 回调使用的本地 HTTP 回调页
├── Misc/               头像缓存、线程调度、语言和序列化辅助
├── ModInterop/         CollabUtils2、SpeedrunTool、Extended Variants 等兼容层
└── ModFolder/          Everest manifest、Dialog、音频、贴图和 shader
```

## 运行时分层

```text
Celeste/Everest Hook (MiaoNetModule)
        |
Components (Main, Chat, PlayerList, Emote, DebugMap, Status)
        |
MiaoNetContext (生命周期、队列、事件、包分发、Request-Response)
        |
ClientState / OnlinePlayer / OnlineChannel
        |
MiaoServerConnection (TLS/TCP，位于 MiaoNet.ClientShared)
        |
MiaoNet.Shared (包、数据结构、二进制序列化)
```

## MiaoNetContext

`MiaoNetContext` 是客户端中枢，负责：

1. 在游戏主线程驱动组件的 `Update` 和 `Render`。
2. 启动和取消名为 `MiaoNet Connection` 的连接线程。
3. 保存 `MiaoServerConnection`、`ClientState`、`PooledStringManager` 和待处理请求。
4. 将收到的包交给 `PacketDispatcher`，把响应包按 `RequestID` 回调给发起者。
5. 在断线时清空接收队列、请求、状态、头像缓存状态并通知所有组件。

连接线程会创建 `SingleThreadedSynchronizationContext` 与对应的 TaskScheduler，依次完成 TLS/TCP 建连、版本检查、认证握手，再等待首个 `PacketClientInitial`。初始化必须回到游戏主线程执行；之后连接线程持续收包和发包，任一管线结束都会触发断线清理。

## 线程与队列

```text
游戏主线程
  MiaoNetContext.Update() -> mainThreadQueue -> receiveQueue -> PacketDispatcher -> Components
  MiaoNetContext.Render() -> 可渲染组件

连接线程
  MiaoServerConnection.ReceivePacketsLoopAsync()
  MiaoServerConnection.SendPacketsLoopAsync()
```

| 方向 | 机制 | 用途 |
|---|---|---|
| 连接线程 -> 主线程 | `receiveQueue` | 排队处理普通收到的包 |
| 连接线程 -> 主线程 | `mainThreadQueue` | 初始化、状态提示、头像纹理准备等必须在主线程执行的动作 |
| 主线程 -> 连接线程 | `MiaoServerConnection.QueuePacket` | 将包放入发送队列 |
| 连接线程内直接处理 | `HandleDirectPacket` | Ping 立即回复 Pong；可异步准备新玩家头像 |
| 主线程内 | `pendingRequests` | 用 `RequestID` 分发 `PacketResponse` |

普通收包在主线程每帧最多处理 64 个或约 1 ms，避免网络突发占满整帧；未处理的包保持原序留到下一帧。Debug 构建默认不执行逐包 JSON 追踪，需要诊断时显式设置 `EnablePacketTracing=true`。连续 Watch delta 会先合并到本帧待应用状态，不逐包写控制台。

## 组件

所有组件继承 `MiaoNetComponent`，由上下文统一管理生命周期：

| 组件 | 主要职责 |
|---|---|
| `MainComponent` | 每帧发送 `PacketPlayerFrame`，处理位置/房间变化、Ghost、观战和互动 |
| `ChatComponent` | 输入框、聊天标签页、历史记录、消息收发和命令执行 |
| `PlayerListComponent` | 按频道显示在线玩家及频道 |
| `EmoteComponent` | 表情轮盘与表情发送 |
| `DebugMapComponent` | Debug Map 场景中的覆盖渲染 |
| `StatusComponent` | 连接、认证、断线和错误状态提示 |

`ChatComponent` 会在连接时创建 `Global`、`Channel`、`Map` 三个标签页。`ChatMessageFactory` 将服务端消息转换为 ChatInputBox 的富文本，支持提及高亮和私聊回执；断线时会清理标签页、消息和输入历史。

## 状态模型

`ClientState` 从 `PacketClientInitial` 初始化，包含：

- `Players`：不含自己的在线玩家字典；
- `AllPlayers`：包含 `Self` 的枚举；
- `Channels`：频道字典；
- `Self`、`SelfChannel` 和 `SelfState`；
- 玩家加入、离开、切频道、位置/在场状态和初始状态更新方法。

位置使用 `PlayerLocation`：它由 `PlayerMapLocation`（地图 SID、章节模式）和房间名组成，支持空位置、Debug Map、正常地图，以及 `None`、`Incremental`、`FullSync` 三种变化结果。地图变化时，服务端会发送初始状态快照；同地图房间变化只更新位置。

## 同步与 Ghost

`MainComponent` 构造 `PlayerStateDelta` 并通过 `PacketPlayerFrame` 发送位置、动画、缩放、冲刺、Follower、持有物和风向的变化。存在 Watcher 时，同一帧包额外携带 Player 的最终 Camera 世界坐标；Watcher 在非转场阶段以该坐标作为唯一镜头目标，转场仍由原版 `Level.TransitionTo` 独占 Camera。服务端转发为 `PacketContextualPlayerNotification<PacketPlayerFrame>`，客户端据此更新对应 `MiaoNetGhost`。

Ghost 由 `MiaoNetGhost` 和 `MiaoNetGhostEntity` 表示，并组合名称标签、表情、Follower、死亡体、头发和持有物渲染。地图切换或离开地图会创建/销毁 Ghost；`GroupPhotoPlatform`、`Fireworks`、`EmoteWheel` 等实体由对应组件按状态管理。

## Everest 集成点

`MiaoNetModule` 在 Everest 生命周期中注册/注销 Hook，并把上下文接入游戏循环。当前集成覆盖：

- `Engine.Update` 和 `Engine.RenderCore`：驱动上下文；
- `Level.OnLoadLevel`、`Level.OnExit`：发送 `PacketPlayerLocationChanged`；
- `Player.Die`、`PlayerDeadBody.End`、`Player.Added`：分别同步死亡表现、原版死亡 WipeOut 的真实起点和复活；普通死亡和 Retry 在 Player 重生后发送一次带生命周期标记的轻量完整实体 Replace，Watcher 与 Player 同时开始缩圈，在全黑帧等待并强制恢复实体与周期相位，随后直接整屏亮起而不额外播放 WipeIn；Touch Switch 完成态优先按地图 ID 原地重建，其他无法逆转的单向实体才在同一黑屏帧触发一次房间重建兜底；死亡后直接换房的特殊流程会把复活延迟到目标房间就绪后无动画应用；
- `Everest.Events.Level.OnTransitionTo`：记录 Player 实际的 source、target、出口位置和原版方向，Watcher 等待目标房间 Replace 后驱动原版平滑转场；
- `Player.Play`：同步玩家音频；
- `PlayerCollider.Check`：互动/观战时调整碰撞；
- 设置菜单、Debug 控制台命令和与其他 Mod 的兼容 Hook。

## 连接与认证

Debug 构建定义 `USE_LOCALHOST_PFX`，客户端默认连接 `127.0.0.1:21473`，并使用嵌入的 `localhost.pfx`。Release 构建默认连接 `s.saplonily.top:21473`，验证服务器证书。认证实现由 `UseCeleMiaoAuth` 选择：关闭时将 `PlayerInfo` 作为自定义认证数据，开启时走 CeleMiao OAuth 和 `ClientRC` 本地回调。
