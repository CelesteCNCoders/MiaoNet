# RFC: Scope Tree — Unified State Management & Broadcasting

> **Navigation / 导航**
> - [English](#english)
> - [中文](#中文)

---

<a id="english"></a>

# [EN] Scope Tree

Status: Draft | Author: wheatEars | Date: 2026-06-15

## Problem

Player location is tracked in 4 places (`ServerPlayer.Channel`, `ServerChannel.players`, `ServerMapUnit.players`, `ServerPlayer.Location`) that must stay in sync manually. Each PacketHandler assembles broadcast targets ad-hoc with different predicate patterns. Adding a new scope level (e.g., Room) means auditing every handler.

## Solution

A single tree is the source of truth for "who is where." Broadcast targets are derived from tree queries.

```
RootScope
├── ChannelScope "main" [permanent]
│   ├── MapScope "forest" → {Alice, Bob}
│   └── MapScope "town" → {Carol}
└── ChannelScope "custom"
    └── MapScope "forest" → {Dave}
```

Each connection has exactly one attachment point (deepest scope). Scope membership is implied by tree path, not stored separately.

## Key Properties

**Extensible.** New scope level = new node type inserted in hierarchy. Existing handlers work unchanged—`AllConnections` recurses automatically.

```
MapScope "forest"
└── RoomScope "room-3"   ← new level, zero handler changes
    └── {Alice}
```

**Lazy recomputation.** Tree mutates only on scope-changing packets (map switch, channel move, join, leave—low frequency). Frame sync (high frequency) reads a precomputed immutable set `scope.Connections`—O(1) lookup, no locking, no tree traversal.

**Single mutation primitive.** Join, leave, switch channel, switch map all reduce to `Move(connection, targetScope)`. Side effects (empty node GC, group recomputation) happen in one place.

## Implementation Shape

```csharp
public abstract class Scope { /* Parent, Children, Connections, AllConnections */ }
public sealed class RootScope : Scope { }
public sealed class ChannelScope : Scope { ChannelInfo Info; }
public sealed class MapScope : Scope { PlayerMap Map; ReaderWriterLockSlim StateLock; }
```

```csharp
public interface IPlayerScopeManager
{
    void AddPlayer(MiaoClientConnection connection);
    void RemovePlayer(MiaoClientConnection connection);
    MoveResult MoveToChannel(MiaoClientConnection connection, int channelId);
    MoveResult MoveToMap(MiaoClientConnection connection, PlayerMap map);

    IReadOnlyCollection<MiaoClientConnection> GetScopePeers(MiaoClientConnection connection);
    IReadOnlyCollection<MiaoClientConnection> GetChannelMembers(MiaoClientConnection connection);
}
```

`MoveResult` returns precomputed target sets (NewPeers, PreviousPeers, Others) so handlers don't assemble them manually.

## Feature Flag

Compile-time `.csproj` switch. Two `PacketHandling` files (old/new) never coexist in one build. Shared files (`Handshake`, `Connection`, `ServerPlayer`) stay untouched.

```xml
<DefineConstants Condition="$(UseScopeTree)=='true'">$(DefineConstants);USE_SCOPE_TREE</DefineConstants>
```

## Migration

1. Extract `IPlayerScopeManager`, wrap existing logic as legacy impl
2. Build ScopeTree core + unit tests
3. Write new PacketHandling against `IPlayerScopeManager`
4. Wire `.csproj` switch, validate both compile
5. Integration test with MockClient, confirm identical broadcast behavior
6. Delete old code

---
---

<a id="中文"></a>

# [中文] Scope Tree

状态：Draft | 作者：wheatEars | 日期：2026-06-15

## 问题

玩家位置在 4 处重复记录（`ServerPlayer.Channel`、`ServerChannel.players`、`ServerMapUnit.players`、`ServerPlayer.Location`），需手动同步。每个 PacketHandler 用不同的 predicate 模式手写广播目标。加新作用域层级（如 Room）需要逐个审查 handler。

## 方案

用一棵树作为"谁在哪"的唯一事实来源。广播目标从树查询派生。

```
RootScope
├── ChannelScope "main" [permanent]
│   ├── MapScope "forest" → {Alice, Bob}
│   └── MapScope "town" → {Carol}
└── ChannelScope "custom"
    └── MapScope "forest" → {Dave}
```

每个连接在树中有且只有一个挂载点（最深层 scope）。作用域归属由树路径隐含，不再单独存储。

## 关键特性

**可扩展。** 新作用域 = 在层级中插入新节点类型。现有 handler 不需改动——`AllConnections` 自动向下递归。

```
MapScope "forest"
└── RoomScope "room-3"   ← 新层级，handler 零改动
    └── {Alice}
```

**惰性重算。** 树仅在作用域变化包到达时修改（切地图、切频道、加入、退出——低频）。帧同步（高频）读预计算的不可变集合 `scope.Connections`——O(1) 查找，无锁，无树遍历。

**单一变更原语。** 加入、退出、切频道、切地图全部归结为 `Move(connection, targetScope)`。副作用（空节点回收、组播组重算）集中在一处。

## 实现骨架

```csharp
public abstract class Scope { /* Parent, Children, Connections, AllConnections */ }
public sealed class RootScope : Scope { }
public sealed class ChannelScope : Scope { ChannelInfo Info; }
public sealed class MapScope : Scope { PlayerMap Map; ReaderWriterLockSlim StateLock; }
```

```csharp
public interface IPlayerScopeManager
{
    void AddPlayer(MiaoClientConnection connection);
    void RemovePlayer(MiaoClientConnection connection);
    MoveResult MoveToChannel(MiaoClientConnection connection, int channelId);
    MoveResult MoveToMap(MiaoClientConnection connection, PlayerMap map);

    IReadOnlyCollection<MiaoClientConnection> GetScopePeers(MiaoClientConnection connection);
    IReadOnlyCollection<MiaoClientConnection> GetChannelMembers(MiaoClientConnection connection);
}
```

`MoveResult` 返回预计算的目标集合（NewPeers、PreviousPeers、Others），handler 不再自行组装。

## 特性开关

编译期 `.csproj` 开关。两个 `PacketHandling` 文件（新/旧）不会同时参与编译。共用文件（`Handshake`、`Connection`、`ServerPlayer`）不动。

```xml
<DefineConstants Condition="$(UseScopeTree)=='true'">$(DefineConstants);USE_SCOPE_TREE</DefineConstants>
```

## 迁移路径

1. 抽出 `IPlayerScopeManager`，现有逻辑包装为 legacy 实现
2. 实现 ScopeTree 核心 + 单元测试
3. 基于 `IPlayerScopeManager` 写新版 PacketHandling
4. 接入 `.csproj` 开关，验证两套都能编译
5. 用 MockClient 集成测试，确认广播行为一致
6. 删除旧代码
