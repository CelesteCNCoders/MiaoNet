# MiaoNet Client 架构概览

## 项目结构

```
source/MiaoNet.Client/
├── Game/
│   ├── MiaoNetModule.cs                — Everest Mod 入口（Load/Unload/Hook）
│   ├── MiaoNetModuleSettings.cs        — 用户设置（持久化）
│   ├── MenuMiaoNetOptions.cs           — 设置菜单 UI
│   ├── MiaoNetFont.cs                  — 字体加载
│   ├── MiaoNetSFX.cs                   — 音效常量
│   ├── MiaoNetTag.cs                   — ECS Tag
│   ├── MiaoNetCommands.cs              — 调试控制台命令
│   ├── MInputHack.cs                   — 输入劫持工具
│   ├── OuiConflict.cs                  — CelesteNet 冲突检测 UI
│   ├── ScreenClamper.cs                — UI 屏幕边界约束
│   ├── SpriteIDTracker.cs              — Sprite ID 反查
│   └── Settings/                       — 设置枚举（SyncMode, ClipType, ButtonMode 等）
├── Connection/
│   ├── MiaoNetContext.cs               — 核心上下文（组件容器、主循环、收发队列）
│   ├── MiaoNetContext.Connection.cs    — 连接线程（握手、收发 Task）
│   ├── MiaoNetContext.PacketHandling.cs— 包分发 + 事件触发
│   ├── MiaoServerConnection.cs         — 底层 TLS/TCP 连接封装
│   └── ConnectionStatus.cs             — 连接状态消息
├── Data/
│   ├── ClientState.cs                  — 客户端全局状态（玩家列表、频道列表、Self）
│   ├── OnlinePlayer.cs                 — 其他在线玩家数据
│   ├── OnlineChannel.cs                — 频道数据
│   ├── MiaoNetChatText.cs              — 聊天文本格式
│   └── BakedEmoteData.cs               — 表情数据
├── Components/
│   ├── MiaoNetComponent.cs             — 组件基类（Update/Render/OnConnected/OnDisconnected）
│   ├── MainComponent.cs                — 玩家同步核心（帧发送、Ghost 管理）
│   ├── MainComponent.Interactions.cs   — 玩家互动（抓取等）
│   ├── MainComponent.Watching.cs       — 观战模式
│   ├── ChatComponent.cs                — 聊天 UI + 消息收发
│   ├── ChatCompletionProvider.cs       — 聊天自动补全
│   ├── EmoteComponent.cs               — 表情轮盘
│   ├── DebugMapComponent.cs            — Debug Map 覆盖层
│   ├── PlayerListComponent.cs          — 玩家列表 UI
│   ├── PlayerListComponent.Entry.cs    — 玩家列表条目
│   ├── PlayerListComponent.ChannelEntry.cs — 频道条目
│   └── StatusComponent.cs              — 连接状态 UI
├── Entity/
│   ├── MiaoNetEntity.cs                — MiaoNet ECS 实体基类
│   ├── MiaoNetGhost.cs                 — Ghost（其他玩家投影）核心
│   ├── MiaoNetGhostEntity.cs           — Ghost 引擎实体包装
│   ├── GhostNameTag.cs                 — 名字标签
│   ├── GhostEmote.cs                   — Ghost 表情显示
│   ├── GhostFollower.cs                — Ghost 的 Follower
│   ├── GhostDeadBody.cs                — Ghost 死亡体
│   ├── GhostRenderLayerEntity.cs       — 渲染层级控制
│   ├── EmoteWheel.cs                   — 表情选择轮盘实体
│   ├── Fireworks.cs                    — 烟花实体
│   ├── FireworksComponent.cs           — 烟花组件
│   ├── GroupPhotoPlatform.cs           — 合照平台
│   └── IdleHover.cs                    — 空闲悬浮动画
├── Command/
│   ├── MiaoNetCommand.cs               — 聊天命令系统入口
│   ├── MiaoNetCommand.Commands.cs      — 具体命令实现（/tp, /rtp, /channel 等）
│   ├── MiaoNetCommand.Context.cs       — 命令执行上下文
│   └── CommandParser.cs                — 命令解析器
├── ClientRC/
│   └── ClientRC.cs                     — 本地 HTTP 回调（认证码接收）
├── Misc/
│   ├── AvatarManager.cs                — 头像下载与缓存
│   ├── SingleThreadedSynchronizationContext.cs — 连接线程同步上下文
│   ├── SingleThreadedTaskScheduler.cs  — 连接线程 TaskScheduler
│   ├── ChatChannelMatcher.cs           — 聊天频道匹配
│   ├── GameLanguage.cs                 — 语言工具
│   ├── TokenDataUtils.cs               — Token 辅助
│   └── ...
└── ModInterop/
    ├── CollabUtils2Interop.cs          — Collab Utils 2 兼容
    └── SpeedrunToolInterop.cs          — SpeedrunTool 兼容
```

## 分层设计

```
┌─────────────────────────────────────────────────┐
│  游戏引擎层 (MiaoNetModule — Everest Hook)       │  IL Hook / On Hook / 事件
├─────────────────────────────────────────────────┤
│  组件层 (MainComponent / ChatComponent / ...)    │  游戏逻辑 + UI 渲染
├─────────────────────────────────────────────────┤
│  上下文层 (MiaoNetContext)                       │  包分发、事件总线、组件容器
├─────────────────────────────────────────────────┤
│  数据层 (ClientState / OnlinePlayer / ...)       │  客户端状态管理
├─────────────────────────────────────────────────┤
│  网络层 (MiaoServerConnection)                   │  TLS/TCP 收发
└─────────────────────────────────────────────────┘
```

## 线程模型

```
游戏主线程 (Monocle Engine)
    │
    ├── MiaoNetContext.Update()     — 消费 receiveQueue / mainThreadQueue
    ├── MiaoNetContext.Render()     — 渲染组件
    └── 组件 Update/Render

连接线程 (MiaoNet Connection)
    │
    ├── StartConnectionAsync        — 握手流程
    ├── DoReceivingAndProcessingAsync — 收包 → HandleDirectPacket / receiveQueue
    └── SendPacketsLoopAsync        — sendChannel 批量写出

SingleThreadedSynchronizationContext  — 连接线程的 SyncCtx，支持 async/await + Post
```

### 线程间通信

| 方向 | 机制 | 用途 |
|------|------|------|
| 连接线程 → 主线程 | `receiveQueue` (ConcurrentQueue) | 收到的包排队等主线程处理 |
| 连接线程 → 主线程 | `mainThreadQueue` (ConcurrentQueue\<Action>) | 连接状态变更、头像加载等 |
| 主线程 → 连接线程 | `MiaoServerConnection.QueuePacket` | 发包投递 |
| 连接线程特殊处理 | `HandleDirectPacket` | Ping 直接回复 Pong，不入队 |

## 连接生命周期

```
MiaoNetContext.Connect()
    │
    ▼
ConnectionThread 启动（新线程 + SingleThreadedSynchronizationContext）
    │
    ▼
MiaoServerConnection.CreateAsync (TCP + TLS)
    │
    ▼
Version Check (发送本地版本，校验服务端版本)
    │
    ▼
Handshake (发送认证数据，接收 HandshakeAckData)
    │
    ▼
接收 PacketClientInitial → mainThreadQueue 通知主线程创建 ClientState
    │
    ▼
主线程 OnConnected() → 组件初始化
    │
    ▼
Receive + Send Task 并行运行
    │
    ▼
任一 Task 结束 / 取消 → OnDisconnected() → 清理状态
```

## MiaoNetContext 核心上下文

客户端的中枢，职责：

1. **组件容器** — 持有所有 `MiaoNetComponent` 实例，驱动其 Update/Render
2. **包分发** — `PacketDispatcher` 将收到的包路由到 `HandlePacket` 重载
3. **事件总线** — 暴露 C# event（`PlayerJoined`、`PlayerMapChanged` 等），组件订阅
4. **Request-Response** — `pendingRequests` 字典管理异步请求回调
5. **连接管理** — 启动/断开连接线程，维护 `MiaoServerConnection` 生命周期

### 包处理流程

```
连接线程收包
    │
    ├── HandleDirectPacket (Ping → 立即 Pong，不入队)
    │
    └── receiveQueue.Enqueue
            │
            ▼
        主线程 Update()
            │
            ├── PacketResponse → pendingRequests 回调
            │
            └── 其他包 → PacketDispatcher → HandlePacket 重载
                    │
                    └── 更新 ClientState + 触发事件
```

## 组件系统

所有组件继承 `MiaoNetComponent`，由 `MiaoNetContext` 统一驱动：

| 组件 | 职责 |
|------|------|
| `MainComponent` | 帧同步发送、Ghost 创建/销毁/更新、位置变更处理 |
| `ChatComponent` | 聊天 UI、消息收发、命令解析 |
| `EmoteComponent` | 表情轮盘 UI、表情发送 |
| `PlayerListComponent` | 玩家列表 UI（按频道分组） |
| `DebugMapComponent` | Debug Map 模式下的覆盖渲染 |
| `StatusComponent` | 连接状态消息显示 |

### MainComponent 核心逻辑

- **帧同步发送**：每帧构建 `PacketPlayerFrame`（位置、动画、Follower、Holdable、风向等），投递到发送队列
- **Ghost 管理**：根据 `ShouldSyncFrom` 判定是否需要为某玩家创建 Ghost 实体
- **位置变更**：监听 `MiaoNetModule.PlayerLocationChanged` 事件，发送 `PacketPlayerMapChanged`
- **响应处理**：收到 `PacketPlayerMapChangedResponse` 后批量创建 Ghost

## 数据模型

### ClientState

初始化自 `PacketClientInitial`，维护：
- `players: Dictionary<int, OnlinePlayer>` — 其他在线玩家
- `channels: Dictionary<int, OnlineChannel>` — 频道列表
- `Self: OnlinePlayer` — 自己

### OnlinePlayer

```csharp
int ID
OnlineChannel Channel
PlayerInfo Info
PlayerLocation Location
PlayerState? State
PlayerGraphicsInfo? GraphicsInfo
PlayerGlobalFlags GlobalFlags
int LastPing
```

### OnlineChannel

```csharp
int ID
ChannelInfo Info
HashSet<OnlinePlayer> Players
```

## 与游戏引擎的集成

客户端作为 Everest Mod 运行，通过 MonoMod 的 IL/On Hook 接入 Celeste 引擎：

| Hook | 用途 |
|------|------|
| `Engine.Update` (IL) | 注入 `MiaoNetContext.Update()` |
| `Engine.RenderCore` (IL) | 注入 `MiaoNetContext.Render()` |
| `Level.OnLoadLevel` | 触发 `PlayerLocationChanged` |
| `Level.OnExit` | 触发 `PlayerLocationChanged(Empty)` |
| `Player.Die` | 触发 `PlayerDied` |
| `Player.Added` | 处理传送后的 spawn 位置 |
| `Player.Play` | 触发 `PlayerSoundPlayed`（音频同步） |
| `PlayerCollider.Check` | 被抓取 / 观战时跳过碰撞 |

## Ghost 渲染

`MiaoNetGhost` 是 `MiaoNetEntity`（继承 Celeste 的 `Entity`），表示其他玩家在本地场景中的投影：

- 接收 `PacketPlayerFrame` 更新位置、动画、朝向
- 播放 PlayerSprite 动画、管理 Follower/Holdable/Hair
- 处理死亡/复活动画（`GhostDeadBody`）
- 名字标签（`GhostNameTag`）+ 表情气泡（`GhostEmote`）

## 命令系统

聊天输入以 `/` 开头时进入命令模式：

- `CommandParser` 解析命令名和参数
- `MiaoNetCommand.Commands.cs` 实现具体命令（`/tp`, `/rtp`, `/channel`, `/emote` 等）
- 命令在主线程执行，可访问完整 `MiaoNetContext`
