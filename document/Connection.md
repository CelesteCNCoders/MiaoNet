# MiaoNet 连接流程

本文档详细描述 MiaoNet 客户端与服务器之间的连接建立过程和状态管理。

更详细的协议说明请参考 [通信协议文档 (Protocol.md)](./Protocol.md)。

## 连接流程概览

```mermaid
sequenceDiagram
    participant C as 客户端
    participant T as TCP
    participant S as 服务器
    participant O as 其他客户端
    
    Note over C,S: 阶段 1: TCP 连接建立
    C->>T: 连接请求
    T->>S: TCP 三次握手
    S->>C: 连接建立
    
    Note over C,S: 阶段 2: 协议握手
    C->>S: ConnectionHead (魔数)
    C->>S: HandshakeData
    
    S->>S: 验证版本
    S->>S: 验证 Mod 列表
    
    alt 验证成功
        S->>S: 创建玩家对象
        S->>S: 分配频道
        S->>C: HandshakeAckData
        S->>C: PacketClientInitial
        
        Note over C: 初始化客户端状态
        C->>C: 创建 MiaoNetContext
        C->>C: 初始化玩家列表
        
        Note over C,O: 阶段 3: 广播新玩家
        S->>O: PacketPlayerJoined
        
        Note over C,S: 阶段 4: 正常通信
        C<<->>S: 数据包交换
    else 验证失败
        S->>C: 拒绝原因
        S->>C: 断开连接
    end
```

## 协议详解

### 1. Handshake & Initialization

> 暂时还没有做 TLS 加密层, 以后加了就是简单套一层

#### 步骤 1: 客户端发起握手

客户端发送 `ConnectionHead` 以及 `HandshakeData`:

**HandshakeData 包含:**
- 客户端版本 (`Version`)
- 语言代码 (`LangCode`)
- 玩家名称 (`Name`)
- 安装的 Mod 列表 (`NetMods[]`)

```mermaid
graph LR
    A[客户端] -->|1. ConnectionHead| B[服务器]
    A -->|2. HandshakeData| B
    B -->|3. 验证| C{是否接受?}
    C -->|接受| D[HandshakeAckData]
    C -->|拒绝| E[拒绝消息+断开]
    D --> F[PacketClientInitial]
```

#### 步骤 2: 服务器验证

服务器根据 `HandshakeData` 信息决定是否接受连接:

**验证项目:**
1. 协议版本是否兼容
2. 玩家名称是否有效
3. Mod 列表是否可接受
4. 服务器是否已满

**接受连接:**
1. 创建 `ServerPlayer` 对象
2. 分配默认频道（通常是频道 0）
3. 发送 `HandshakeAckData` 确认
4. 发送 `PacketClientInitial` 初始化数据

**拒绝连接:**
- 如果 `ConnectionHead` 不正确: 直接断开连接
- 如果验证失败: 发送拒绝原因后断开连接

#### 步骤 3: 客户端初始化

客户端接收 `PacketClientInitial` 并初始化:

**PacketClientInitial 包含:**
- 玩家自身信息: `PlayerInfo` (ID, Name)
- 频道列表: `ChannelInfo[]` (ID, Name)
- 在线玩家列表: `Player[]`
  - `PlayerInfo`: 昵称等元信息
  - `PlayerLocation`: 所在地图、房间名 (MapSid, MapRoom)
  - `PlayerGraphicsInfo?`: 图形信息（可选）
  - `PlayerState?`: 状态信息（可选）

```mermaid
graph TB
    A[接收 PacketClientInitial] --> B[初始化 MiaoNetContext]
    B --> C[设置自身 PlayerInfo]
    B --> D[填充频道列表]
    B --> E[填充玩家列表]
    E --> F[缓存 PlayerGraphicsInfo]
    E --> G[记录玩家位置]
```

#### 步骤 4: 广播新玩家

服务器向其他客户端广播新玩家加入:

```mermaid
sequenceDiagram
    participant N as 新玩家
    participant S as 服务器
    participant O1 as 玩家1
    participant O2 as 玩家2
    
    N->>S: 连接完成
    
    par 广播给所有在线玩家
        S->>O1: PacketPlayerJoined
        and
        S->>O2: PacketPlayerJoined
    end
    
    O1->>O1: 添加到玩家列表
    O2->>O2: 添加到玩家列表
```

### 2. 地图变更导致的同步

#### 位置状态定义

客户端可能处于以下位置状态:

| 状态 | MapSid | MapRoom | 说明 |
|------|--------|---------|------|
| **InMap** | 非空 | 非空 | 在普通地图的房间中 |
| **InDebugMap** | 非空 | 空字符串 | 在调试地图 |
| **None** | 空字符串 | 空字符串 | 不在地图（主菜单等） |

> 注: 可能有 mod 会暂时离开 `Level` 但仍然算作在这个图里

#### 地图变更流程

```mermaid
sequenceDiagram
    participant C as 客户端
    participant S as 服务器
    participant O as 同房间玩家
    
    Note over C: 玩家切换地图/房间
    C->>C: 检测地图变更
    C->>S: PacketPlayerMapChanged
    
    S->>S: 更新玩家位置
    S->>S: 查找同房间玩家
    
    alt 进入有其他玩家的房间
        S->>C: PacketPlayerMapChangedResponse
        
        loop 对每个同房间玩家
            alt 客户端未缓存 GraphicsInfo
                S->>C: PlayerGraphicsInfo
            end
            S->>C: PlayerState
        end
        
        Note over C: 创建 Ghost 实体
        loop 对每个玩家
            C->>C: new MiaoNetGhost
            C->>C: 应用 GraphicsInfo
            C->>C: 应用 State
        end
    end
    
    Note over S,O: 广播给同房间其他玩家
    S->>O: 玩家进入通知
    O->>O: 创建/更新 Ghost
```

#### 详细步骤说明

**客户端发送位置更新:**
```mermaid
graph TD
    A[地图变更事件] --> B{变更类型?}
    B -->|完整地图切换| C[PacketPlayerMapChanged]
    B -->|仅房间切换| D[PacketPlayerMapRoomChanged]
    C --> E[包含 PlayerLocation]
    C --> F[包含 InitialState 可选]
    D --> G[包含新房间名]
```

**服务器处理地图变更:**

1. 更新服务器端的玩家位置记录
2. 确定需要同步的范围:
   - 同频道 + 同地图 + 同房间 = 完整同步
   - 同频道 + 同地图 + 不同房间 = 位置同步
   - 其他 = 仅元信息同步

3. 向客户端发送同房间玩家信息:
   - `GraphicsInfo`: 图形同步信息（仅首次或更新时）
   - `PlayerState`: 位置信息、冲刺状态等

**GraphicsInfo 缓存机制:**

```mermaid
graph TB
    A[玩家 A 进入房间] --> B{客户端已缓存<br/>玩家 B 的 GraphicsInfo?}
    B -->|是| C[只发送 PlayerState]
    B -->|否| D[发送 GraphicsInfo + PlayerState]
    D --> E[客户端缓存 GraphicsInfo]
    E --> F[下次无需重复发送]
```

**优势:**
- 减少带宽使用
- GraphicsInfo 通常不变，缓存后可重复使用
- 除非玩家更新外观，否则不需要重新发送

#### Ghost 实体创建

客户端接收其他玩家的信息后:

```mermaid
graph LR
    A[接收玩家数据] --> B[创建 MiaoNetGhost]
    B --> C[设置位置和状态]
    B --> D[创建 Hair 实体]
    B --> E[设置动画]
    D --> F[应用 GraphicsInfo]
    F --> G[设置头发颜色]
    F --> H[设置头发长度]
```

### 3. 帧同步

建立连接并进入地图后，客户端与服务器开始帧级别的实时交互:

```mermaid
sequenceDiagram
    participant C1 as 客户端1
    participant S as 服务器
    participant C2 as 客户端2
    
    loop 每帧 (~60fps)
        C1->>S: PacketPlayerFrame
        S->>S: 验证和处理
        
        alt 同房间
            S->>C2: PacketPlayerFrame
            C2->>C2: 更新 Ghost 实体
        end
        
        C2->>S: PacketPlayerFrame
        S->>C1: PacketPlayerFrame
        C1->>C1: 更新 Ghost 实体
    end
```

**PacketPlayerFrame 包含:**
- Position: 位置
- AnimationID: 动画 ID
- AnimationFrame: 动画帧
- Scale: 缩放
- Flags: 朝向、冲刺等标志
- Dashes: 冲刺数（条件包含）

## 连接状态机

```mermaid
stateDiagram-v2
    [*] --> Disconnected
    
    Disconnected --> Connecting: 开始连接
    Connecting --> Handshaking: TCP 连接成功
    Connecting --> Disconnected: 连接失败
    
    Handshaking --> Authenticating: 发送握手数据
    Handshaking --> Disconnected: 握手超时
    
    Authenticating --> Initializing: 验证成功
    Authenticating --> Rejected: 验证失败
    
    Initializing --> Connected: 接收初始数据
    Initializing --> Disconnected: 初始化超时
    
    Connected --> Connected: 正常通信
    Connected --> Disconnecting: 主动断开
    Connected --> Disconnected: 连接中断
    
    Rejected --> Disconnected: 显示拒绝原因
    Disconnecting --> Disconnected: 清理完成
    
    Disconnected --> [*]
```

## 同步级别 (Sync Level)

根据玩家间的位置关系，采用不同的同步策略:

```mermaid
graph TD
    A[判断玩家关系] --> B{同频道?}
    B -->|否| C[Level 0: 仅元信息]
    B -->|是| D{同地图?}
    D -->|否| C
    D -->|是| E{在调试地图?}
    E -->|是| F[Level 1: 位置同步]
    E -->|否| G{同房间?}
    G -->|否| F
    G -->|是| H[Level 2: 完整同步]
    
    C --> I[同步: 地图位置]
    F --> J[同步: 位置, 动画ID]
    H --> K[同步: 完整帧数据]
    
    style C fill:#ffe1e1
    style F fill:#fff4e1
    style H fill:#e1f5ff
```

### Level 0: 元信息同步
- **条件**: 不同频道 或 不同地图
- **同步内容**:
  - 地图位置 (MapSid, MapRoom)
  - 频道信息
- **用途**: 玩家列表显示

### Level 1: 位置同步
- **条件**: 同频道 + 同地图 + (在调试地图 或 不同房间)
- **同步内容**:
  - 位置 (Position)
  - 基本动画 ID
- **用途**: 调试地图中看到其他玩家的大概位置

### Level 2: 完整同步
- **条件**: 同频道 + 同地图 + 同房间
- **同步内容**:
  - 完整的 PacketPlayerFrame (位置、动画、缩放、朝向等)
  - 状态标志 (死亡、重生等)
  - 表情
  - 聊天消息
- **用途**: 完整的多人游戏体验

## 断开连接

### 正常断开流程

```mermaid
sequenceDiagram
    participant C as 客户端
    participant S as 服务器
    participant O as 其他客户端
    
    C->>S: 关闭连接
    S->>S: 检测连接断开
    S->>S: 清理玩家数据
    
    par 广播给所有在线玩家
        S->>O: PacketPlayerLeft
    end
    
    O->>O: 移除玩家
    O->>O: 销毁 Ghost 实体
```

### 被踢出流程

```mermaid
sequenceDiagram
    participant A as 管理员
    participant S as 服务器
    participant C as 被踢玩家
    participant O as 其他玩家
    
    A->>S: 踢出命令
    S->>S: 验证权限
    S->>C: PacketGotKicked (原因)
    S->>C: 断开连接
    
    C->>C: 显示被踢原因
    
    par 广播给其他玩家
        S->>O: PacketPlayerLeft
    end
    
    O->>O: 移除玩家
    O->>O: 销毁 Ghost 实体
```

### 超时断开

服务器会检测客户端的活跃度:
- 如果长时间没有收到任何数据包
- 视为连接超时
- 自动断开连接并清理资源

## 错误处理

### 协议错误

| 错误类型 | 处理方式 |
|---------|---------|
| 版本不匹配 | 拒绝连接，返回错误信息 |
| 无效的数据包 ID | 断开连接 |
| 数据包过大 | 断开连接 |
| 反序列化失败 | 忽略数据包或断开连接 |

### 网络错误

| 错误类型 | 客户端处理 | 服务器处理 |
|---------|-----------|-----------|
| 连接超时 | 尝试重连 | 断开连接，清理资源 |
| 心跳超时 | 重新建立连接 | 断开连接 |
| TCP 错误 | 显示错误，返回主菜单 | 断开连接，广播离开消息 |

## 性能考虑

### 带宽使用

**每个玩家的带宽估算 (60fps):**
- 上行: ~1.5 KB/s (PacketPlayerFrame)
- 下行: ~1.5 KB/s × N (N = 同房间玩家数)

**100 人服务器，平均每房间 5 人:**
- 每玩家下行: ~6 KB/s
- 服务器总带宽: ~600 KB/s (上行) + ~600 KB/s (下行) ≈ 1.2 MB/s

### 延迟优化

1. **客户端预测**: 本地立即响应输入
2. **服务器权威**: 最终状态由服务器决定
3. **插值**: 平滑其他玩家的移动
4. **优先级**: 重要数据包优先发送

## 相关文档

- [通信协议 (Protocol.md)](./Protocol.md) - 详细的协议说明
- [架构设计 (Architecture.md)](./Architecture.md) - 系统架构
- [数据包参考 (PacketReference.md)](./PacketReference.md) - 所有数据包定义
