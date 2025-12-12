# MiaoNet 架构设计

本文档详细描述 MiaoNet 的整体架构设计、模块划分和核心设计理念。

## 系统架构概览

MiaoNet 采用经典的客户端-服务器架构，服务器作为中心节点管理所有客户端连接和数据分发。

```mermaid
graph TB
    subgraph "客户端层 (MiaoNet.Client)"
        C1[客户端 1]
        C2[客户端 2]
        C3[客户端 N...]
    end
    
    subgraph "服务器层 (MiaoNet.Server)"
        S[MiaoNet Server]
        SS[服务器状态管理]
        CH[频道管理]
        PM[玩家管理]
    end
    
    subgraph "共享层 (MiaoNet.Shared)"
        PKT[数据包定义]
        SER[序列化/反序列化]
        DATA[共享数据结构]
    end
    
    C1 <-->|TCP连接| S
    C2 <-->|TCP连接| S
    C3 <-->|TCP连接| S
    
    S --> SS
    S --> CH
    S --> PM
    
    C1 -.->|依赖| PKT
    C2 -.->|依赖| PKT
    C3 -.->|依赖| PKT
    S -.->|依赖| PKT
    
    PKT --> SER
    PKT --> DATA
```

## 项目结构

MiaoNet 项目由以下几个主要模块组成：

### 1. MiaoNet.Shared (共享库)

共享库包含客户端和服务器都需要的代码，确保双方使用相同的数据结构和协议定义。

```mermaid
graph LR
    subgraph "MiaoNet.Shared"
        A[Packet] --> A1[IPacket 接口]
        A --> A2[数据包定义]
        A --> A3[PacketRegistry]
        
        B[Helpers] --> B1[RefBinaryReader]
        B --> B2[RefBinaryWriter]
        B --> B3[PacketHandler]
        
        C[Data] --> C1[PlayerInfo]
        C --> C2[PlayerState]
        C --> C3[PlayerLocation]
        C --> C4[ChannelInfo]
        
        D[Command] --> D1[命令定义]
        D --> D2[命令处理]
    end
```

**主要组件：**

- **Packet/**: 所有数据包定义和注册表
  - `IPacket`: 数据包接口
  - `PacketRegistry`: 数据包类型注册和反射机制
  - `Packets/`: 具体的数据包实现
- **Helpers/**: 辅助工具类
  - `RefBinaryReader/Writer`: 高性能的二进制序列化
  - `PacketHandler`: 数据包处理器框架
- **Data/**: 共享数据结构
  - 玩家信息、状态、位置等核心数据类型
- **Command/**: 游戏内命令系统

### 2. MiaoNet.Server (服务器)

服务器负责管理所有客户端连接、频道、玩家状态同步和消息广播。

```mermaid
graph TB
    subgraph "MiaoNet.Server"
        A[Server] --> A1[MiaoServerService]
        A --> A2[MiaoClientConnection]
        
        B[Data] --> B1[ServerState]
        B --> B2[ServerPlayer]
        B --> B3[ServerChannel]
        B --> B4[MiaoClientSession]
        
        C[Http] --> C1[MiaoHttpService]
        C --> C2[管理API]
        
        D[Primitives] --> D1[Vector2]
        D --> D2[Color]
        D --> D3[AreaMode]
    end
    
    A1 --> B1
    A2 --> B4
    B1 --> B2
    B1 --> B3
```

**主要组件：**

- **Server/**: 核心服务器逻辑
  - `MiaoServerService`: 服务器主服务，管理所有连接
  - `MiaoClientConnection`: 单个客户端连接的管理
- **Data/**: 服务器特有数据
  - `ServerState`: 全局服务器状态（不可变设计，原子更新）
  - `ServerPlayer`: 服务器端的玩家表示
  - `ServerChannel`: 频道管理
- **Http/**: HTTP 管理接口
  - 提供服务器管理后台 API
- **Primitives/**: 基础类型定义
  - 服务器独有的结构体和枚举

**并发控制设计：**

服务器采用乐观并发控制策略：
- 在线玩家列表、频道列表等使用不可变类型
- 通过原子操作进行状态更新
- 减少锁的使用，提高并发性能

### 3. MiaoNet.Client (客户端)

客户端作为 Celeste 游戏的 Mod 运行，负责与服务器通信、渲染其他玩家和处理用户交互。

```mermaid
graph TB
    subgraph "MiaoNet.Client"
        A[Connection] --> A1[MiaoServerConnection]
        A --> A2[MiaoNetContext]
        A --> A3[数据包处理]
        
        B[Components] --> B1[MainComponent]
        B --> B2[ChatComponent]
        B --> B3[PlayerListComponent]
        B --> B4[EmoteComponent]
        B --> B5[DebugMapComponent]
        
        C[Entity] --> C1[MiaoNetGhost]
        C --> C2[MiaoNetGhostEmote]
        
        D[Data] --> D1[ClientState]
        D --> D2[OnlinePlayer]
        D --> D3[OnlineChannel]
        
        E[Game] --> E1[游戏逻辑集成]
    end
    
    A1 --> A2
    A2 --> B
    B --> C
    A2 --> D
```

**主要组件：**

- **Connection/**: 网络连接管理
  - `MiaoServerConnection`: 与服务器的 TCP 连接
  - `MiaoNetContext`: 客户端上下文，管理在线状态
- **Components/**: UI 和功能组件
  - `MainComponent`: 主控制组件
  - `ChatComponent`: 聊天功能
  - `PlayerListComponent`: 玩家列表显示
  - `EmoteComponent`: 表情系统
  - `DebugMapComponent`: 调试地图特殊处理
- **Entity/**: 游戏实体
  - `MiaoNetGhost`: 其他玩家的实体表示
  - `MiaoNetGhostEmote`: 玩家表情实体
- **Data/**: 客户端特有数据
  - `ClientState`: 客户端状态
  - `OnlinePlayer`: 在线玩家表示

**线程模型：**

客户端使用单线程模型：
- 所有游戏逻辑在主线程执行
- 网络 I/O 在独立线程，通过队列与主线程通信
- 使用 `SingleThreadedSynchronizationContext` 确保线程安全

### 4. ChatInputBox (聊天组件)

独立的聊天输入和历史记录库，可被其他项目复用。

## 数据流架构

### 客户端到服务器

```mermaid
sequenceDiagram
    participant C as 客户端
    participant N as 网络层
    participant S as 服务器
    participant B as 广播系统
    
    C->>C: 创建数据包
    C->>C: 序列化 (RefBinaryWriter)
    C->>N: 发送字节流
    N->>S: TCP 传输
    S->>S: 反序列化 (RefBinaryReader)
    S->>S: PacketRegistry 解析
    S->>S: 处理数据包
    
    alt 需要广播
        S->>B: 准备广播
        B->>B: 序列化一次
        B->>N: 发送给多个客户端
    end
```

### 服务器到客户端

```mermaid
sequenceDiagram
    participant S as 服务器
    participant B as 广播系统
    participant N as 网络层
    participant C1 as 客户端1
    participant C2 as 客户端2
    
    S->>S: 创建数据包
    S->>B: SerializedPacket (共享缓冲区)
    B->>B: 序列化一次
    
    par 并行广播
        B->>N: 发送给客户端1
        N->>C1: TCP 传输
        and
        B->>N: 发送给客户端2
        N->>C2: TCP 传输
    end
    
    C1->>C1: 反序列化
    C1->>C1: 处理数据包
    C2->>C2: 反序列化
    C2->>C2: 处理数据包
    
    B->>B: 引用计数归零，回收缓冲区
```

## 核心设计理念

### 1. 高性能序列化

使用 `RefBinaryReader` 和 `RefBinaryWriter` 实现零分配或低分配的序列化：
- 使用 `ref struct` 避免堆分配
- 使用 `Span<byte>` 进行高效的内存操作
- 利用栈内存 (`stackalloc`) 减少 GC 压力

### 2. 数据包注册系统

通过 `PacketRegistry` 实现自动的数据包类型注册：
- 编译时通过 Attribute 标记数据包类型
- 运行时自动生成类型 ID 到反序列化器的映射
- 支持快速的数据包解析和派发

### 3. 不可变状态设计

服务器端采用不可变数据结构：
- 状态更新通过创建新对象而非修改现有对象
- 使用原子操作 (`Interlocked`) 进行状态切换
- 简化并发控制，避免复杂的锁机制

### 4. 优化的广播机制

服务器广播使用 `SerializedPacket`:
- 序列化一次，发送多次
- 使用 `ArrayPool` 管理内存
- 引用计数自动回收缓冲区

### 5. 分级同步机制

根据玩家位置关系采用不同的同步策略：

```mermaid
graph TD
    A[玩家关系判断] --> B{是否同频道?}
    B -->|否| C[SyncLevel 0: 仅元信息]
    B -->|是| D{是否同地图?}
    D -->|否| C
    D -->|是| E{是否在调试地图?}
    E -->|是| F[SyncLevel 1: 位置同步]
    E -->|否| G{是否同房间?}
    G -->|否| F
    G -->|是| H[SyncLevel 2: 完整同步]
    
    C --> I[同步: 地图位置, 频道]
    F --> J[同步: 位置, 动画ID]
    H --> K[同步: 完整状态, 帧数据]
```

**同步级别说明：**
- **SyncLevel 0**: 只同步基本信息（所在地图、房间名）
- **SyncLevel 1**: 同步位置信息，但不同步详细状态
- **SyncLevel 2**: 完整的帧级同步，包括所有动画和状态

## 扩展性设计

### 组件化架构

客户端使用组件化设计，便于功能扩展：
- 每个功能作为独立的 Component
- 通过 `MiaoNetContext` 进行组件通信
- 可以轻松添加新的功能组件

### 命令系统

支持可扩展的命令系统：
- 命令定义与实现分离
- 支持命令参数解析
- 服务器和客户端都可以定义命令

### Mod 支持（计划中）

为未来的 Mod 支持预留接口：
- 握手阶段交换 Mod 列表
- 支持自定义数据包
- 支持自定义命令

## 性能考虑

### 内存管理

- 使用对象池减少 GC 压力
- 避免不必要的字符串分配
- 使用 `Span<T>` 和 `Memory<T>` 进行零拷贝操作

### 网络优化

- 数据包大小限制在 64KB 以内
- 批量发送减少系统调用
- 使用紧凑的二进制格式

### 并发处理

- 服务器使用异步 I/O
- 无锁数据结构减少竞争
- 工作负载在线程池中分散

## 安全性考虑

### 当前实现

- 基于 TCP 的可靠传输
- 客户端版本验证
- 基本的 Mod 列表验证

### 未来计划

- TLS 加密层（计划中）
- 更完善的身份认证
- 防作弊机制

## 相关文档

- [通信协议 (Protocol.md)](./Protocol.md) - 详细的协议说明
- [连接流程 (Connection.md)](./Connection.md) - 连接建立过程
- [数据结构 (DataStructures.md)](./DataStructures.md) - 数据结构参考
