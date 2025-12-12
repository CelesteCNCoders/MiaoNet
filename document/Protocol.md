# MiaoNet 通信协议

本文档详细描述 MiaoNet 客户端与服务器之间的通信协议、数据格式和传输机制。

## 协议概览

MiaoNet 使用基于 TCP 的自定义二进制协议进行通信。协议设计目标：
- **高效**: 紧凑的二进制格式，最小化带宽使用
- **可靠**: 基于 TCP 保证数据可靠传输
- **可扩展**: 支持未来的协议扩展和新功能

### 协议栈

```mermaid
graph TB
    A[应用层] --> B[数据包层]
    B --> C[序列化层]
    C --> D[传输层 - TCP]
    D --> E[网络层 - IP]
    
    style A fill:#e1f5ff
    style B fill:#fff4e1
    style C fill:#ffe1e1
    style D fill:#e1ffe1
    style E fill:#f0f0f0
```

## 数据包格式

### 基本结构

每个数据包都遵循以下格式：

```
+----------------+----------------+------------------+
|  Size (2 byte) | Packet ID (2B) |  Payload (N B)   |
+----------------+----------------+------------------+
|    UInt16      |    UInt16      |   Variable       |
|  Little Endian | Little Endian  |                  |
+----------------+----------------+------------------+
```

**字段说明：**

1. **Size** (2 字节, UInt16): 
   - 表示后续数据的长度（Packet ID + Payload）
   - 不包括 Size 字段本身
   - 最大值 65535，意味着单个数据包最大约 64KB

2. **Packet ID** (2 字节, UInt16):
   - 数据包类型的唯一标识符
   - 通过 `PacketRegistry` 自动分配
   - ID 0 保留未使用
   - ID 从 1 开始按注册顺序递增

3. **Payload** (可变长度):
   - 数据包的实际内容
   - 使用 `RefBinaryWriter` 序列化
   - 格式取决于具体的数据包类型

### 数据包生命周期

```mermaid
graph LR
    A[创建数据包对象] --> B[序列化]
    B --> C[添加Size和ID头]
    C --> D[发送到TCP流]
    D --> E[网络传输]
    E --> F[接收TCP流]
    F --> G[读取Size和ID]
    G --> H[反序列化Payload]
    H --> I[创建数据包对象]
    I --> J[处理数据包]
```

## 序列化机制

### IRefBinarySerializable 接口

所有可序列化的类型都实现此接口：

```csharp
public interface IRefBinarySerializable
{
}

public interface IRefBinarySerializable<T> : IRefBinarySerializable 
    where T : IRefBinarySerializable<T>
{
    void Serialize(ref RefBinaryWriter writer);
    static abstract T Deserialize(ref RefBinaryReader reader);
}
```

### RefBinaryWriter 和 RefBinaryReader

使用 `ref struct` 实现的高性能序列化器：

**优势：**
- 零堆分配（在栈上分配）
- 使用 `Span<byte>` 进行高效内存操作
- Little Endian 字节序，保证跨平台一致性

**支持的基本类型：**

| C# 类型 | 大小 | 序列化方法 |
|---------|------|------------|
| `byte` | 1 字节 | `Write(byte)` |
| `bool` | 1 字节 | `Write(bool)` |
| `short` / `Int16` | 2 字节 | `Write(Int16)` |
| `ushort` / `UInt16` | 2 字节 | `Write(UInt16)` |
| `int` / `Int32` | 4 字节 | `Write(Int32)` |
| `uint` / `UInt32` | 4 字节 | `Write(UInt32)` |
| `long` / `Int64` | 8 字节 | `Write(Int64)` |
| `ulong` / `UInt64` | 8 字节 | `Write(UInt64)` |
| `float` / `Single` | 4 字节 | `Write(Single)` |
| `double` / `Double` | 8 字节 | `Write(Double)` |
| `Half` | 2 字节 | `Write(Half)` |

**支持的复合类型：**

| 类型 | 格式 | 说明 |
|------|------|------|
| `string` | `UInt16 (length)` + UTF-8 bytes | 最大 65535 字节 |
| `Version` | `UInt16` (major) + `UInt16` (minor) + `UInt16` (build) | 三段版本号 |
| `Color` | 4 bytes (R, G, B, A) | RGBA 颜色 |
| `Vector2` | `Single` (X) + `Single` (Y) | 2D 向量 |
| `Array<T>` | `UInt16` (count) + T[] | 最多 65535 个元素 |

### 7-Bit 编码整数（暂未使用）

协议支持但当前未广泛使用的优化：
- `Write7BitEncodedInt` / `Read7BitEncodedInt`
- `Write7BitEncodedInt64` / `Read7BitEncodedInt64`
- 用于压缩小整数，节省空间

## 数据包注册系统

### PacketRegistry

`PacketRegistry` 负责管理所有数据包类型：

```mermaid
graph TB
    A[编译时] --> B[PacketRegistryAttribute]
    B --> C[标记所有数据包类型]
    
    D[运行时] --> E[Assembly扫描]
    E --> F[收集所有Attribute]
    F --> G[生成类型映射]
    G --> H[ID -> Deserializer]
    G --> I[Type -> ID]
    
    J[发送] --> I
    I --> K[写入Packet ID]
    
    L[接收] --> H
    H --> M[调用Deserializer]
    M --> N[创建数据包对象]
```

**工作流程：**

1. **注册阶段**（静态构造函数）：
   - 扫描 Assembly 中的 `PacketRegistryAttribute`
   - 为每个数据包类型分配唯一 ID（从 1 开始）
   - 创建 ID 到反序列化器的映射
   - 创建类型到 ID 的映射

2. **序列化阶段**：
   ```csharp
   PacketRegistry.WritePacket(packet, ref writer);
   // 1. 查找 packet 类型对应的 ID
   // 2. 写入 ID
   // 3. 调用 packet.Serialize()
   ```

3. **反序列化阶段**：
   ```csharp
   IPacket packet = PacketRegistry.ReadPacket(id, ref reader);
   // 1. 根据 ID 查找反序列化器
   // 2. 调用反序列化器创建对象
   ```

### 数据包标记

使用 `PacketRegistryAttribute` 标记数据包类型：

```csharp
[assembly: PacketRegistry(typeof(PacketPlayerFrame))]
[assembly: PacketRegistry(typeof(PacketChatMessage))]
// ...
```

这确保了客户端和服务器使用相同的 ID 映射。

## 握手协议

握手是建立连接的第一步，用于版本验证和初始化。

```mermaid
sequenceDiagram
    participant C as 客户端
    participant S as 服务器
    
    Note over C,S: 阶段 1: TCP 连接建立
    C->>S: TCP SYN
    S->>C: TCP SYN-ACK
    C->>S: TCP ACK
    
    Note over C,S: 阶段 2: 协议握手
    C->>S: ConnectionHead (魔数)
    C->>S: HandshakeData (版本, 用户名, Mod列表)
    
    alt 验证成功
        S->>S: 验证版本兼容性
        S->>S: 检查Mod列表
        S->>S: 分配频道
        S->>S: 创建玩家对象
        S->>C: HandshakeAckData (确认)
        S->>C: PacketClientInitial (初始数据)
        
        Note over C: 初始化完成，进入正常通信
        
        S->>C: 其他玩家信息
        C->>S: 开始发送状态更新
    else 验证失败
        S->>C: 拒绝原因 (如果协议头正确)
        S->>C: 断开连接
        Note over C: 显示错误信息
    end
```

### HandshakeData 结构

```csharp
public sealed class HandshakeData
{
    public Version Version { get; }      // 客户端版本
    public byte LangCode { get; }        // 语言代码
    public string Name { get; }          // 玩家名称
    public IReadOnlyList<NetMod> NetMods { get; }  // 已安装的Mod列表
    
    public sealed class NetMod
    {
        public Version Version { get; set; }  // Mod版本
        public string Name { get; set; }      // Mod名称
    }
}
```

### PacketClientInitial 结构

服务器发送的初始化数据包，包含客户端需要的所有初始信息：

```csharp
public sealed class PacketClientInitial
{
    public PlayerInfo SelfPlayerInfo { get; }              // 自己的玩家信息
    public IReadOnlyCollection<ChannelInfo> Channels { get; }  // 频道列表
    public IReadOnlyCollection<Player> Players { get; }    // 当前在线玩家列表
    
    public struct Player
    {
        public int ChannelID { get; }                 // 所在频道
        public PlayerInfo PlayerInfo { get; }         // 玩家基本信息
        public PlayerLocation Location { get; }       // 位置信息
        public PlayerGraphicsInfo? GraphicsInfo { get; }  // 图形信息（可选）
        public PlayerState? State { get; }            // 状态信息（可选）
    }
}
```

## 连接流程详解

### 完整的连接建立流程

```mermaid
stateDiagram-v2
    [*] --> Disconnected: 初始状态
    
    Disconnected --> Connecting: 开始连接
    Connecting --> Handshaking: TCP连接成功
    
    Handshaking --> Authenticating: 发送握手数据
    
    Authenticating --> Rejected: 验证失败
    Authenticating --> Initializing: 验证成功
    
    Initializing --> Connected: 接收初始数据
    
    Connected --> Connected: 正常通信
    Connected --> Disconnecting: 主动断开/超时
    
    Rejected --> Disconnected
    Disconnecting --> Disconnected
    
    Disconnected --> [*]
```

### 连接状态说明

1. **Disconnected**: 未连接状态
2. **Connecting**: 正在建立 TCP 连接
3. **Handshaking**: 正在发送连接头
4. **Authenticating**: 正在进行身份验证
5. **Initializing**: 正在接收初始数据
6. **Connected**: 已连接，正常通信中
7. **Disconnecting**: 正在断开连接
8. **Rejected**: 连接被服务器拒绝

## 帧同步协议

### 玩家帧数据同步

```mermaid
sequenceDiagram
    participant C1 as 客户端1
    participant S as 服务器
    participant C2 as 客户端2
    participant C3 as 客户端3
    
    Note over C1,C3: 所有玩家在同一房间
    
    loop 每帧 (60fps)
        C1->>C1: 更新玩家状态
        C1->>S: PacketPlayerFrame
        
        S->>S: 验证数据
        S->>S: 更新玩家状态
        
        par 广播给同房间玩家
            S->>C2: PacketPlayerFrame
            and
            S->>C3: PacketPlayerFrame
        end
        
        C2->>C2: 更新Ghost实体
        C3->>C3: 更新Ghost实体
    end
```

### PacketPlayerFrame 结构

```csharp
public sealed class PacketPlayerFrame
{
    public Vector2 Position { get; }         // 玩家位置
    public ushort AnimationID { get; }       // 动画ID
    public ushort AnimationFrame { get; }    // 动画帧
    public Vector2 Scale { get; }            // 缩放
    public FrameFlags Flags { get; }         // 标志位
    public byte Dashes { get; set; }         // 冲刺数（条件包含）
    
    [Flags]
    public enum FrameFlags : ushort
    {
        FacingLeft = 1 << 0,    // 朝向左
        StartDash = 1 << 1,     // 开始冲刺
        EndDash = 1 << 2,       // 结束冲刺
        DashesChange = 1 << 3   // 冲刺数改变
    }
}
```

**优化点：**
- 总大小约 26 字节（不含冲刺数变化时）
- 使用标志位减少条件字段
- 只在冲刺数变化时才传输该字段

### 地图变更同步

当玩家切换地图或房间时：

```mermaid
sequenceDiagram
    participant C as 客户端
    participant S as 服务器
    participant O as 其他客户端
    
    C->>C: 检测到地图变更
    C->>S: PacketPlayerMapChanged
    
    S->>S: 更新玩家位置
    S->>S: 确定影响范围
    
    alt 进入新房间
        S->>S: 查找同房间玩家
        S->>C: PacketPlayerMapChangedResponse (同房间玩家列表)
        
        loop 对于每个同房间玩家
            S->>C: 玩家GraphicsInfo (如未缓存)
            S->>C: 玩家State
        end
        
        S->>O: 广播玩家加入
    else 离开房间
        S->>O: PacketPlayerLeft (广播给原房间玩家)
    end
```

## 聊天协议

### 聊天消息流程

```mermaid
sequenceDiagram
    participant C1 as 发送者
    participant S as 服务器
    participant C2 as 接收者1
    participant C3 as 接收者2
    
    C1->>S: PacketSendChatMessage (消息内容)
    
    S->>S: 验证消息
    S->>S: 确定接收范围
    
    alt 普通聊天
        par 广播给同频道所有人
            S->>C1: PacketChatMessage
            and
            S->>C2: PacketChatMessage
            and
            S->>C3: PacketChatMessage
        end
    else 私聊
        S->>C1: PacketChatMessage (确认)
        S->>C2: PacketChatMessage (目标玩家)
    else 命令
        S->>S: 执行命令
        S->>C1: PacketChatMessage (命令结果)
    end
```

### PacketChatMessage 结构

```csharp
public sealed class PacketChatMessage
{
    public ChatMessageType Type { get; set; }     // 消息类型
    public int? SourcePlayer { get; set; }        // 发送者ID（可选）
    public string Content { get; set; }           // 消息内容
}

public enum ChatMessageType : byte
{
    Chat,           // 普通聊天
    PrivateMessage, // 私聊
    Server,         // 服务器消息
    ServerChat      // 服务器聊天
}
```

## 优化的广播机制

### SerializedPacket

服务器使用 `SerializedPacket` 优化广播性能：

```mermaid
graph TB
    A[创建数据包] --> B[创建SerializedPacket]
    B --> C[序列化一次]
    C --> D[从ArrayPool租用缓冲区]
    D --> E[设置引用计数 = 接收者数量]
    
    E --> F1[发送给客户端1]
    E --> F2[发送给客户端2]
    E --> F3[发送给客户端N]
    
    F1 --> G1[OnConsumed: 计数-1]
    F2 --> G2[OnConsumed: 计数-1]
    F3 --> G3[OnConsumed: 计数-1]
    
    G1 --> H{计数==0?}
    G2 --> H
    G3 --> H
    
    H -->|是| I[归还缓冲区到ArrayPool]
    H -->|否| J[继续等待]
```

**优势：**
- 序列化一次，发送多次
- 自动内存管理，无内存泄漏
- 减少 GC 压力

## 频道管理协议

### 频道切换流程

```mermaid
sequenceDiagram
    participant C as 客户端
    participant S as 服务器
    participant O1 as 原频道玩家
    participant O2 as 新频道玩家
    
    C->>S: PacketPlayerChannelMove (目标频道ID)
    
    S->>S: 验证频道是否存在
    
    alt 频道有效
        S->>S: 更新玩家频道
        S->>O1: PacketPlayerLeft (离开通知)
        S->>C: PacketPlayerChannelMove (确认)
        S->>O2: PacketPlayerJoined (加入通知)
        
        S->>C: 发送新频道玩家列表
    else 频道无效
        S->>C: PacketResponse (错误)
    end
```

## 表情系统协议

### 表情发送流程

```mermaid
sequenceDiagram
    participant C as 客户端
    participant S as 服务器
    participant O as 其他客户端
    
    C->>S: PacketEmote (表情ID, 位置)
    
    S->>S: 验证表情ID
    
    alt 有效的表情
        S->>O: PacketEmote (广播给同房间玩家)
        O->>O: 显示表情气泡
    else 无效的表情
        S->>C: PacketResponse (错误)
    end
```

## 断开连接协议

### 正常断开

```mermaid
sequenceDiagram
    participant C as 客户端
    participant S as 服务器
    participant O as 其他客户端
    
    C->>S: 断开TCP连接
    S->>S: 检测连接断开
    S->>S: 清理玩家数据
    S->>O: PacketPlayerLeft (广播)
    O->>O: 移除Ghost实体
```

### 被踢出

```mermaid
sequenceDiagram
    participant A as 管理员
    participant S as 服务器
    participant C as 被踢玩家
    participant O as 其他客户端
    
    A->>S: 踢出命令
    S->>S: 验证权限
    S->>C: PacketGotKicked (原因)
    S->>C: 断开TCP连接
    S->>O: PacketPlayerLeft (广播)
    
    C->>C: 显示被踢原因
    O->>O: 移除Ghost实体
```

## 协议扩展性

### 未来计划

1. **UDP 支持**:
   - `PacketFlags.PreferUdp` 标志已预留
   - 用于低延迟的帧数据传输
   - TCP 作为可靠通道的备份

2. **TLS 加密**:
   - 在 TCP 层之上添加 TLS
   - 保护数据传输安全
   - 防止中间人攻击

3. **压缩**:
   - 对大数据包进行压缩
   - 减少带宽使用
   - 可选的压缩算法

## 性能指标

### 典型数据包大小

| 数据包类型 | 大小（字节） | 频率 |
|-----------|------------|------|
| PacketPlayerFrame | ~26 | 60 次/秒 |
| PacketChatMessage | ~20-100 | 按需 |
| PacketPlayerJoined | ~50 | 按需 |
| PacketPlayerLeft | ~8 | 按需 |
| PacketEmote | ~20 | 按需 |

### 带宽估算

**每个玩家（60fps 帧同步）：**
- 发送: 26 字节 × 60 = 1.56 KB/秒 ≈ 12.5 Kbps
- 接收: 26 字节 × 60 × N (N=同房间玩家数)

**100 个玩家，平均每房间 5 人：**
- 每玩家接收: 26 × 60 × 4 = 6.24 KB/秒 ≈ 50 Kbps
- 服务器总流量: ~6 Mbps (上行+下行)

## 错误处理

### 协议错误

- **版本不匹配**: 拒绝连接
- **无效的数据包 ID**: 断开连接
- **数据包过大**: 断开连接
- **反序列化失败**: 忽略数据包或断开连接

### 网络错误

- **连接超时**: 自动重连（客户端）
- **心跳超时**: 断开连接（服务器）
- **TCP 错误**: 断开连接，清理资源

## 相关文档

- [架构设计 (Architecture.md)](./Architecture.md) - 系统架构
- [数据结构 (DataStructures.md)](./DataStructures.md) - 数据结构详解
- [数据包参考 (PacketReference.md)](./PacketReference.md) - 所有数据包定义
