# RFC: Scope Tree — 统一状态管理与广播

状态：Implemented | 作者：wheatEars | 日期：2026-06-15 | 更新：2026-06-30

## 问题

玩家位置在 4 处重复记录（`ServerPlayer.Channel`、`ServerChannel.players`、`ServerMapUnit.players`、`ServerPlayer.Location`），需手动同步。每个 PacketHandler 用不同的 predicate 模式手写广播目标。加新作用域层级（如 Room）需要逐个审查 handler。

## 方案

用一棵树作为"谁在哪"的唯一事实来源。广播目标从树查询派生。

```
GlobalScope [permanent]
├── ChannelScope "main"
│   ├── MapScope (forest A)  → {Alice, Bob}
│   └── MapScope (town A)    → {Carol}
└── ChannelScope "custom"
    └── MapScope (forest A)  → {Dave}
```

每个玩家在树中有且只有一个挂载点（最深层 scope）。作用域归属由树路径隐含，不再单独存储。

## 树结构

```
GlobalScope                        — 全局根节点，permanent
└── ChannelScope                   — key: int (channelId)
    └── MapScope                   — key: PlayerMap (值类型，Sid+AreaMode)
        └── RoomScope              — key: string (roomId)，预留
```

### Scope 基类

```csharp
abstract class Scope
    Connections          : ImmutableHashSet<ServerPlayer>  // 直接挂载的玩家
    AllConnections       : ImmutableHashSet<ServerPlayer>  // 含子树，dirty flag 惰性重算
    abstract ChildScopes : IEnumerable<Scope>              // 由泛型子类提供

abstract class Scope<TSelfKey, TChildKey> : Scope
    Children : ImmutableDictionary<TChildKey, Scope>       // ImmutableInterlocked CAS 更新
```

`AllConnections` 带 dirty flag 向上冒泡：任何 AddConnection/RemoveConnection/AddChild/RemoveChild 都会沿 parent 链标脏，下次读取时重算。

## ScopeTree

所有结构变更通过 `ScopeTree` 统一管理，用 `ReaderWriterLockSlim` (`treeLock`) 保护。

### Move 系列方法

| 方法 | 用途 |
|------|------|
| `MovePlayerToMap(player, map)` | 在当前 channel 下 ensure + move，原子操作 |
| `MovePlayerToMapInChannel(player, map, channel)` | 指定 channel 下 ensure + move |
| `MovePlayerToChannel(player, channel)` | 切频道，若在地图中则在新 channel 重建 MapScope |
| `MovePlayer(player, target)` | 通用 move，直接移动到指定 scope |

所有 Move 返回 `MoveResult(PreviousPeers, NewPeers)`，handler 直接使用，不再自行组装广播目标。

### EnsureMapScope

`ChannelScope.EnsureMapScope(PlayerMap)` 查找或创建 MapScope。在 `MovePlayerToMap` / `MovePlayerToMapInChannel` 中被 `treeLock` 保护，确保 ensure + move 原子执行，防止 Cleanup 在间隙删除空 scope。

### Cleanup

空的非 permanent scope 自底向上回收：从变更的 scope 开始，沿 parent 链检查 `IsEmpty`，逐层 RemoveChild。

## 锁模型

| 之前 | 之后 |
|------|------|
| `MiaoServerService.stateLock` (全局写锁) | `ScopeTree.treeLock` (保护树结构变更) |
| `ServerMapUnit.StateLock` (地图级读写锁) | `MapScope` 内置 `Channel<Func<Task>>` 单线程消费 |

帧同步热路径：投递到 `mapScope.PostAsync()`，单线程顺序执行，无锁竞争。

## 数据模型变化

### 删除

- `ServerPlayer.Channel` / `ServerPlayer.ChannelId` → `ScopeTree.ChannelOf(player)` 查找
- `ServerChannel.Players` / `ServerChannel.MapUnits` → scope 树管理
- `ServerMapUnit` 类 → `PlayerMap`（值类型）直接做 MapScope 的 key
- `IChannelView` / `ChannelViewAdapter`
- `IMiaoServerService` 接口

### 新增

- `ServerPlayer.Scope` — 由 ScopeTree 管理的挂载点
- `ServerChannel.Scope` — 对应的 ChannelScope

### ServerState

薄封装层，move 方法直接委托 ScopeTree：

```csharp
public MoveResult MovePlayerToChannel(player, channel) => ScopeTree.MovePlayerToChannel(player, channel);
public MoveResult MovePlayerToMap(player, map)         => ScopeTree.MovePlayerToMap(player, map);
```

## Handler 改动

### PacketPlayerFrame（帧同步）

```csharp
var mapScope = serverState.ScopeTree.MapOf(player);
await mapScope.PostAsync(() => { /* 改状态 + 广播 */ });
```

### PacketPlayerMapChanged（切地图）

- `IsEmpty` → `MovePlayer(player, channelScope)` 回到频道层
- `IsInDebugMap` / `IsInMap` → `MovePlayerToMap(player, map)`
- 同地图玩家收带 state 的通知，其余玩家收 location-only 通知，两组互斥不覆盖

### PacketPlayerChannelMove（切频道）

- `IsEmpty` → `MovePlayerToChannel`
- `IsInMap` → `MovePlayerToChannel`（内部自动在新 channel 重建 MapScope）
- 同地图玩家收带 state 的通知，其余玩家收简单通知，两组互斥不覆盖

### PacketSendChatMessage（聊天）

- Global → `BroadcastAsync` 全服
- Channel → `ChannelOf(player).AllConnections`
- Map → `MapOf(player).Connections`

### ShouldSyncFrom

```csharp
// 之前：四个条件判断
// 之后：一次引用比较
if (other.Location.IsInDebugMap) return false;
return player.Scope is MapScope && player.Scope == other.Scope;
```

## 待完成

- [ ] RoomScope handler 接入
- [ ] `stateLock` 从 `MiaoServerService` 中完全移除
- [ ] `ServerMapUnit.cs` 删除（已无引用）
