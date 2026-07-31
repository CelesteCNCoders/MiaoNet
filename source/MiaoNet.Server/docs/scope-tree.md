# `Scope Tree` 树状消息作用域管理模型

在 MiaoNet "收包-广播"的服务模型中，需要将玩家位置信息、玩家频道更改、玩家聊天消息等消息依据一定的规则广播给作用域内的其他玩家。

MiaoNet目前的树状消息作用域共分为四层：
```
Global
|
Channel(Public)
|
Map
|
Room(invalid)
```
Map和Room对应游戏内的章节、单面这两个元素，Global和Channel为~~方便玩家组成小团体~~而设立的层级。

## 行为描述

### 登录

玩家登录时，由`ServerPlayer`作为玩家的标识符，挂载在`Public`这一默认 Channel 下，作为判断广播目标的依据。若玩家连线时已经处于地图内，则后续的位置更新包会将连接向下挂载到正确的节点。

### 位置移动

玩家位置移动时，将移动的目标作用域交由`ScopeTree`进行移动操作，并自动生成未初始化的作用域节点。移动过程返回两个玩家集合，对应移动前后作用域内的玩家，以便广播离开/加入消息。

```csharp
MoveResult { PreviousPeers, NewPeers }
```

`ScopeTree`提供以下 Move 方法：

| 方法 | 行为 |
|------|------|
| `MovePlayerToMap(player, map)` | 在当前 Channel 下查找或创建 MapScope，移入 |
| `MovePlayerToMapInChannel(player, map, channel)` | 在指定 Channel 下查找或创建 MapScope，移入 |
| `MovePlayerToChannel(player, channel)` | 切频道；若玩家在地图中，自动在新频道重建同地图的 MapScope |
| `MovePlayer(player, target)` | 直接移动到任意 scope（仅用于退出地图回到 ChannelScope） |

所有 Move 操作在 `treeLock`（写锁）下原子执行。

### 更新玩家状态

更新玩家状态（帧同步）通过`MapScope`内置的工作队列保证线程安全：

```csharp
await mapScope.PostAsync(() => {
    // 修改 player.State
    // 广播 PacketPlayerFrame
});
```

工作队列为 `Channel<Func<Task>>`，单消费者顺序执行，无锁竞争。

### 聊天消息路由

| ChatChannel | 广播范围 |
|-------------|----------|
| Global | `BroadcastAsync` 全服 |
| Channel | `ChannelOf(player).AllConnections` |
| Map | `MapOf(player).Connections` |

### 空节点回收

当玩家离开某个 scope 后，`ScopeTree`会从该节点开始向上检查：若节点为空（无直连玩家、无子节点）且非 permanent，则自动从父节点中移除。

## 数据结构

### Scope 基类

```csharp
abstract class Scope
    Connections          // 直接挂载的玩家（ImmutableHashSet）
    AllConnections       // 含子树所有玩家，dirty flag 惰性重算
    abstract ChildScopes // 由泛型子类实现

abstract class Scope<TSelfKey, TChildKey> : Scope
    Children             // ImmutableDictionary<TChildKey, Scope>
```

`AllConnections`的 dirty flag 沿 parent 链向上冒泡：任何连接变更或子节点变更都标脏所有祖先。

### 各层级

| 层级 | Key 类型 | 特殊行为 |
|------|----------|----------|
| GlobalScope | — | permanent，不会被回收 |
| ChannelScope | int (channelId) | `EnsureMapScope(PlayerMap)` 查找或创建 |
| MapScope | PlayerMap (值类型) | 内置工作队列 |
| RoomScope | string (roomId) | 预留 |

### EnsureMapScope

`ChannelScope.EnsureMapScope(PlayerMap)` 在 children 中查找 `PlayerMap` 值相等的 MapScope，没有则创建。该方法在 ScopeTree 的 `treeLock` 保护下被调用，确保并发进入同一张地图时只创建一个 MapScope。

## 广播设计约束

当一个玩家的位置发生变化时，需要通知两组人：

1. **同地图玩家**（NewPeers）：收到带完整 state 的通知，用于渲染投影
2. **其他所有玩家**：收到仅含 location 的通知，用于更新列表

这两组必须互斥——同一个玩家不能同时收到两种包，否则后发的 location-only 包会覆盖先发的 state 包。

## 与旧实现的对比

| 旧 | 新 |
|----|----|
| 玩家位置分散在 ServerPlayer.Channel、ServerChannel.Players、ServerMapUnit.Players 中 | 玩家仅有 `ServerPlayer.Scope` 一个挂载点 |
| `ServerMapUnit.StateLock`（读写锁） | MapScope 工作队列（无锁） |
| handler 手写广播目标 | Move 返回 `MoveResult`，handler 直接使用 |
| 加新层级需审查所有 handler | 加新层级仅需新 scope 类型，`AllConnections` 自动递归 |