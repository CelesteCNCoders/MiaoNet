# MiaoNet.Shared 包系统

## 概述

MiaoNet 使用自定义二进制协议通信。所有包类型在 Shared 层定义，客户端和服务端共享同一套序列化/反序列化代码。

## 线格式

```
┌──────────┬──────────┬──────────────────┐
│ size     │ id       │ payload          │
│ u16 LE   │ u16 LE   │ [size] bytes     │
└──────────┴──────────┴──────────────────┘
```

- `size`：payload 长度（不含 header 的 4 字节）
- `id`：包类型 ID（从 1 开始，0 保留）

## 包注册

### PacketRegistryAttribute

在 `AssemblyInfo.cs` 中声明所有包类型的有序列表：

```csharp
[assembly: PacketRegistry([
    typeof(PacketClientInitial),    // id = 1
    typeof(PacketPlayerJoined),     // id = 2
    typeof(PacketPlayerLeft),       // id = 3
    ...
])]
```

ID 由数组下标 + 1 决定。新增包只能追加在末尾，不能插入中间。

### PacketRegistry（静态类）

启动时反射读取 `PacketRegistryAttribute`，构建：
- `idToReader: FrozenDictionary<ushort, RefBinaryPacketReadHandler>` — 按 ID 查反序列化方法
- `typeToId: FrozenDictionary<Type, ushort>` — 按类型查 ID

反序列化方法通过接口映射 (`GetInterfaceMap`) 找到静态 `Deserialize` 方法并缓存为委托。

## 包接口层次

```
IContextualPacket                    — 需要序列化上下文
├── IContextualPacket<T>             — 泛型自身类型约束（提供 Deserialize）
├── IContextlessPacket               — 无需上下文（Serialize 忽略 context 参数）
│   └── IContextlessPacket<T>        — 泛型自身类型约束
└── PacketRequest<TResponse>         — 请求包基类（带 RequestID）
    └── PacketResponse               — 响应包基类（带 RequestID）
```

### IContextualPacket

需要 `IPacketSerializationContext`（提供 `PooledStringManager`）才能序列化/反序列化。用于包含 PooledString 的包（如 `PacketPlayerFrame`）。

### IContextlessPacket

不依赖上下文，可独立序列化。大多数简单包属于这类。

### PacketRequest / PacketResponse

实现 Request-Response 模式。发送方为请求分配递增的 `RequestID`，接收方回复时携带相同 ID。用于：
- Ping / Pong
- TeleportRequest / TeleportResponse
- BeTeleportedRequest / BeTeleportedResponse
- SendPrivateChatMessage / Response

## 包装类（泛型通知包）

避免为每种 "服务端转发给其他客户端" 的包都写一个 Notification 变体：

```csharp
PacketPlayerNotification<TPacket>            — 无上下文，附加 PlayerID
PacketContextualPlayerNotification<TPacket>  — 需上下文，附加 PlayerID
```

客户端发 `PacketPlayerFrame`，服务端转发为 `PacketContextualPlayerNotification<PacketPlayerFrame>`，自动附加发送者 ID。

## 序列化基础设施

### RefBinaryWriter / RefBinaryReader

基于 `ref struct` 的零分配二进制读写器，直接操作 `Span<byte>` 或 `MemoryStream`。

### IRefBinarySerializable / IContextualRefBinarySerializable

```csharp
interface IRefBinarySerializable<T>
{
    void Serialize(ref RefBinaryWriter writer);
    static abstract T Deserialize(ref RefBinaryReader reader);
}

interface IContextualRefBinarySerializable<T, TContext>
{
    void Serialize(ref RefBinaryWriter writer, TContext context);
    static abstract T Deserialize(ref RefBinaryReader reader, TContext context);
}
```

### PooledString

高频字符串（动画名等）的优化：首次传输完整字符串并分配 ID，后续只传 ID（2 字节）。

`PooledStringManager` 维护双向映射：
- 发送端：`string → id`（本地分配）
- 接收端：`id → string`（对端分配）

服务端使用 `ImmutableDictionary`（并发安全），客户端使用普通 `Dictionary`（单线程）。

`KnownPooledStrings` 预注册高频字符串，双端启动时即加入映射，避免首次传输开销。

## PacketFlags

```csharp
[Flags]
public enum PacketFlags
{
    None = 0,
    PreferUdp = 1 << 0  // 该包倾向于 UDP 传输（未实现）
}
```

## 包分发

### PacketHandlerRegister + PacketDispatcher

```csharp
PacketHandlerRegister r = new();
r.Register<PacketPlayerFrame>(HandlePacket);
r.Register<PacketChatMessage>(HandlePacket);
PacketDispatcher dispatcher = new(r);

// 分发时：
dispatcher.DispatchPacket(packet);  // 客户端（同步）
dispatcher.DispatchPacketAsync(connection, packet);  // 服务端（异步）
```

按包的运行时类型查找注册的 handler 并调用。

## 完整包列表

### 连接生命周期

| 包 | 方向 | 含义 |
|----|------|------|
| `PacketClientInitial` | S→C | 登录后初始状态（玩家列表、频道列表、自身信息） |
| `PacketPlayerJoined` | S→C | 新玩家加入通知 |
| `PacketPlayerLeft` | S→C | 玩家离开通知 |
| `PacketDisconnected` | S→C | 断开连接（附带原因） |
| `PacketPing` | S→C | 心跳请求 |
| `PacketPong` | C→S | 心跳响应 |
| `PacketPingData` | S→C | 全服延迟数据广播 |

### 位置与同步

| 包 | 方向 | 含义 |
|----|------|------|
| `PacketPlayerFrame` | C→S | 帧数据（位置、动画、Follower、Holdable 等） |
| `PacketPlayerMapChanged` | C→S | 切地图请求（附带 InitialState） |
| `PacketPlayerMapChangedNotification` | S→C | 切地图通知（可选 State + GraphicsInfo） |
| `PacketPlayerMapChangedResponse` | S→C | 切地图响应（同地图玩家列表） |
| `PacketPlayerMapRoomChanged` | C→S | 切房间 |
| `PacketPlayerLiveState` | C→S | 生命状态变更（死亡/复活） |
| `PacketUpdateGlobalFlag` | C→S | 全局标志更新（暂停/打字/直播模式等） |
| `PacketPlayerGraphicsUpdate` | C→S | 图形信息更新 |

### 频道

| 包 | 方向 | 含义 |
|----|------|------|
| `PacketPlayerChannelMove` | C→S | 切频道请求 |
| `PacketPlayerChannelMovedResponse` | S→C | 切频道响应（新频道同地图玩家） |
| `PacketPlayerChannelMovedNotification` | S→C | 切频道通知（可选 State + GraphicsInfo） |
| `PacketChannelCreateAndJoin` | C→S | 创建并加入频道 |
| `PacketChannelCreated` | S→C | 新频道创建通知 |

### 聊天

| 包 | 方向 | 含义 |
|----|------|------|
| `PacketSendChatMessage` | C→S | 发送聊天（指定频道） |
| `PacketChatMessage` | S→C | 聊天消息（含类型、发送者、时间） |
| `PacketSendPrivateChatMessage` | C→S | 私聊请求 |
| `PacketSendPrivateChatMessageResponse` | S→C | 私聊响应 |

### 互动

| 包 | 方向 | 含义 |
|----|------|------|
| `PacketTeleportRequest` | C→S | 传送请求 |
| `PacketTeleportResponse` | S→C | 传送响应 |
| `PacketBeTeleportedRequest` | S→C | 被传送请求（转发给目标） |
| `PacketBeTeleportedResponse` | C→S | 被传送响应（携带 Session 数据） |
| `PacketPlayerGrabPlayer` | C→S / S→C | 抓取/释放玩家 |
| `PacketPlayerGrabJumpOut` | C→S / S→C | 抓取跳出 |

### 表达

| 包 | 方向 | 含义 |
|----|------|------|
| `PacketSendEmote` | C→S | 发送表情 |
| `PacketEmote` | S→C | 表情通知 |
| `PacketSendEmoteText` | C→S | 发送文字表情 |
| `PacketEmoteText` | S→C | 文字表情通知 |
| `PacketPlayerPlayedAudio` | C→S | 音频播放同步 |
| `PacketCreateFireworks` | C→S | 放烟花 |
